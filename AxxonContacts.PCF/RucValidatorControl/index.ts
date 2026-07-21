import { IInputs, IOutputs } from "./generated/ManifestTypes";

// ── Tipos de la API de TURUC ─────────────────────────────────────────────────

interface ContribuyenteDto {
    doc: number;
    razonSocial: string;
    dv: number;
    ruc: string;
    estado: string;
    esPersonaJuridica: boolean;
    esEntidadPublica: boolean;
}

interface ContribuyenteResponse {
    data: ContribuyenteDto | null;
    message: string;
}

// ── Mapeo estado API → valor OptionSet axx_fiscalstate ───────────────────────
// Normalizado a MAYÚSCULAS para comparación case-insensitive
// (la API puede devolver "ACTIVO", "Activo" o "activo")

const ESTADO_MAP: Record<string, number> = {
    "ACTIVO":      1,
    "SUSPENDIDO":  2,
    "CANCELADO":   3,
    "BLOQUEADO":   4,
    "NO VIGENTE":  5,
};

// ── Nombres de campos en Dataverse ───────────────────────────────────────────

const FIELD_GOVERNMENT_ID  = "governmentid";
const FIELD_FISCAL_STATE   = "axx_fiscalstate";
const FIELD_DESCRIPTION    = "description";

export class RucValidatorControl implements ComponentFramework.StandardControl<IInputs, IOutputs> {

    private _context:            ComponentFramework.Context<IInputs>;
    private _notifyOutputChanged: () => void;
    private _container:          HTMLDivElement;

    // UI elements
    private _input:        HTMLInputElement;
    private _button:       HTMLButtonElement;
    private _statusRow:    HTMLDivElement;
    private _statusIcon:   HTMLSpanElement;
    private _statusText:   HTMLSpanElement;

    // Estado interno
    private _currentValue = "";
    private _loading      = false;

    constructor() { /* empty */ }

    // ── init ─────────────────────────────────────────────────────────────────

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        _state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        this._context             = context;
        this._notifyOutputChanged = notifyOutputChanged;
        this._container           = container;

        this._currentValue = context.parameters.DocumentNumber.raw ?? "";
        this._buildUI();
    }

    // ── updateView ───────────────────────────────────────────────────────────

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this._context = context;

        const incoming = context.parameters.DocumentNumber.raw ?? "";
        if (incoming !== this._input.value && !this._loading) {
            this._input.value  = incoming;
            this._currentValue = incoming;
            this._clearStatus();
        }

        this._input.disabled   = context.mode.isControlDisabled || this._loading;
        this._button.disabled  = context.mode.isControlDisabled || this._loading;
    }

    // ── getOutputs ───────────────────────────────────────────────────────────

    public getOutputs(): IOutputs {
        return { DocumentNumber: this._currentValue };
    }

    // ── destroy ──────────────────────────────────────────────────────────────

    public destroy(): void { /* cleanup handled by container removal */ }

    // ── UI builder ───────────────────────────────────────────────────────────

    private _buildUI(): void {
        // Wrapper
        const wrapper = document.createElement("div");
        wrapper.className = "ruc-wrapper";

        // Input row
        const inputRow = document.createElement("div");
        inputRow.className = "ruc-input-row";

        this._input = document.createElement("input");
        this._input.type        = "text";
        this._input.className   = "ruc-input";
        this._input.value       = this._currentValue;
        this._input.placeholder = "Ej: 80012345-0";
        this._input.addEventListener("input",   () => this._onInputChange());
        this._input.addEventListener("keydown",  (e) => { if (e.key === "Enter") this._validate(); });

        this._button = document.createElement("button");
        this._button.type      = "button";
        this._button.className = "ruc-btn";
        this._button.innerHTML = "&#128269; Validar";
        this._button.addEventListener("click", () => this._validate());

        inputRow.appendChild(this._input);
        inputRow.appendChild(this._button);

        // Status row (oculto por defecto)
        this._statusRow  = document.createElement("div");
        this._statusRow.className = "ruc-status hidden";

        this._statusIcon = document.createElement("span");
        this._statusIcon.className = "ruc-status-icon";

        this._statusText = document.createElement("span");
        this._statusText.className = "ruc-status-text";

        this._statusRow.appendChild(this._statusIcon);
        this._statusRow.appendChild(this._statusText);

        wrapper.appendChild(inputRow);
        wrapper.appendChild(this._statusRow);
        this._container.appendChild(wrapper);
    }

    // ── event handlers ───────────────────────────────────────────────────────

    private _onInputChange(): void {
        this._currentValue = this._input.value;
        this._clearStatus();
        this._notifyOutputChanged();
    }

    // ── validacion ───────────────────────────────────────────────────────────

    private async _validate(): Promise<void> {
        const ruc = this._input.value?.trim();
        if (!ruc) {
            this._showStatus("error", "Ingrese un RUC antes de validar.");
            return;
        }

        this._setLoading(true);

        try {
            const contribuyente = await this._callApi(ruc);

            if (!contribuyente) {
                this._showStatus("error", "RUC no encontrado en la SET.");
                return;
            }

            // Actualizar campos del formulario via Xrm
            this._updateFormFields(contribuyente);

            this._showStatus(
                "success",
                `${contribuyente.razonSocial} — ${contribuyente.estado}`
            );

            // Persistir el valor formateado (ej: "80012345-0")
            this._currentValue = contribuyente.ruc ?? ruc;
            this._input.value  = this._currentValue;
            this._notifyOutputChanged();

        } catch (err) {
            const msg = err instanceof Error ? err.message : "Error al conectar con la API.";
            this._showStatus("error", msg);
        } finally {
            this._setLoading(false);
        }
    }

    // ── llamada a la Azure Function ──────────────────────────────────────────

    private async _callApi(ruc: string): Promise<ContribuyenteDto | null> {
        const base   = (this._context.parameters.ApiBaseUrl.raw ?? "").replace(/\/$/, "");
        const apiKey = this._context.parameters.ApiKey.raw ?? "";

        const qs  = apiKey ? `?code=${encodeURIComponent(apiKey)}` : "";
        const url = `${base}/api/turuc/contribuyente/${encodeURIComponent(ruc)}${qs}`;

        const response = await fetch(url, {
            method: "GET",
            headers: { "Accept": "application/json" },
        });

        if (response.status === 404) {
            return null;
        }

        if (!response.ok) {
            throw new Error(`HTTP ${response.status} al consultar la API de TURUC.`);
        }

        const rawText = await response.text();

        let body: ContribuyenteResponse;
        try {
            body = JSON.parse(rawText) as ContribuyenteResponse;
        } catch {
            throw new Error(`Respuesta no es JSON válido: ${rawText.substring(0, 100)}`);
        }

        // Acepta "OK" en cualquier casing y sin importar whitespace
        const messageOk = body.message?.trim().toUpperCase() === "OK";

        if (!body.data || !messageOk) {
            return null;
        }

        return body.data;
    }

    // ── actualizar campos Dataverse via Xrm ─────────────────────────────────

    private _updateFormFields(c: ContribuyenteDto): void {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm = (window as unknown as { Xrm?: typeof Xrm }).Xrm;
        if (!xrm) return;

        const formContext = xrm.Page as Xrm.FormContext | undefined;
        if (!formContext) return;

        // governmentid = ruc formateado (ej: "80012345-0")
        this._setTextField(formContext, FIELD_GOVERNMENT_ID, c.ruc);

        // description = respuesta completa JSON
        this._setTextField(formContext, FIELD_DESCRIPTION, JSON.stringify(c, null, 2));

        // axx_fiscalstate = MultiSelectPicklist — lookup case-insensitive
        const estadoVal = ESTADO_MAP[c.estado?.toUpperCase()];
        if (estadoVal !== undefined) {
            const attr = formContext.getAttribute(FIELD_FISCAL_STATE) as
                Xrm.Attributes.MultiSelectOptionSetAttribute | null;
            if (attr) {
                attr.setValue([estadoVal]);
                attr.fireOnChange();
            }
        }

        // Persona física: descomponer razonSocial en lastname, firstname, middlename
        // Formato: "APELLIDO(S), NOMBRE SEGUNDO_NOMBRE"
        // Ejemplo: "MIRANDA RUIZ DIAZ, JORGE SEBASTIAN"
        //   → lastname="MIRANDA RUIZ DIAZ", firstname="JORGE", middlename="SEBASTIAN"
        if (!c.esPersonaJuridica && !c.esEntidadPublica && c.razonSocial) {
            const { lastname, firstname, middlename } = this._parseRazonSocial(c.razonSocial);
            this._setTextField(formContext, "lastname",   lastname);
            this._setTextField(formContext, "firstname",  firstname);
            this._setTextField(formContext, "middlename", middlename);
        }
    }

    // ── parser de razonSocial ────────────────────────────────────────────────

    private _parseRazonSocial(razonSocial: string): {
        lastname: string;
        firstname: string;
        middlename: string;
    } {
        // Separar por la primera coma: "APELLIDOS, NOMBRES"
        const commaIndex = razonSocial.indexOf(",");

        if (commaIndex === -1) {
            // Sin coma: todo va a lastname
            return { lastname: razonSocial.trim(), firstname: "", middlename: "" };
        }

        const lastname = razonSocial.substring(0, commaIndex).trim();
        const nombres  = razonSocial.substring(commaIndex + 1).trim();

        // Primer token = firstname, el resto = middlename
        const spaceIndex = nombres.indexOf(" ");
        if (spaceIndex === -1) {
            return { lastname, firstname: nombres, middlename: "" };
        }

        const firstname  = nombres.substring(0, spaceIndex).trim();
        const middlename = nombres.substring(spaceIndex + 1).trim();

        return { lastname, firstname, middlename };
    }

    private _setTextField(
        formContext: Xrm.FormContext,
        fieldName: string,
        value: string | null | undefined
    ): void {
        if (!value) return;
        const attr = formContext.getAttribute(fieldName) as
            Xrm.Attributes.StringAttribute | null;
        if (attr) {
            attr.setValue(value);
            attr.fireOnChange();
        }
    }

    // ── helpers UI ───────────────────────────────────────────────────────────

    private _setLoading(loading: boolean): void {
        this._loading          = loading;
        this._button.disabled  = loading;
        this._input.disabled   = loading;
        this._button.innerHTML = loading
            ? "&#9203; Validando..."
            : "&#128269; Validar";
    }

    private _showStatus(type: "success" | "error" | "warning", message: string): void {
        this._statusRow.className  = `ruc-status ruc-status--${type}`;
        this._statusIcon.textContent = type === "success" ? "✅" :
                                       type === "warning" ? "⚠️" : "❌";
        this._statusText.textContent = message;
    }

    private _clearStatus(): void {
        this._statusRow.className    = "ruc-status hidden";
        this._statusText.textContent = "";
    }
}
