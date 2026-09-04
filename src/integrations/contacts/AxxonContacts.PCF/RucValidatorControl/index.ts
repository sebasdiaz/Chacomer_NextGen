import { IInputs, IOutputs } from "./generated/ManifestTypes";

// ── Tipos de la respuesta de la SET ──────────────────────────────────────────
// Fuente: GET /api/set/consulta-ruc de AxxonFiscal, que reenvia el JSON de la
// SET sin re-serializar. La forma la define la DNIT, no nosotros: por eso todo
// es opcional y el codigo no asume que un campo este presente.

interface SetContribuyente {
    razonSocial?:  string;
    /** Estado del contribuyente: "ACTIVO", "CANCELADO", "SUSPENDIDO"... */
    estado?:       string;
    /** "FISICO" o "JURIDICO". Verificado contra INTE el 2026-09-03. */
    tipoPersona?:  string;
    categoria?:    string;
    tipoSociedad?: string;
    rucAnterior?:  string;
    [key: string]: string | undefined;
}

interface SetRucResponse {
    /** "VALIDO" / "INVALIDO": si la consulta encontro el RUC, no el estado del contribuyente. */
    codigo?:        string;
    mensaje?:       string;
    /** Idem codigo. NO es el estado fiscal — ese vive en contribuyente.estado. */
    estado?:        string;
    contribuyente?: SetContribuyente;
}

// ── Nombres de campos en Dataverse ───────────────────────────────────────────
//
// Lo unico que el control escribe fuera del campo al que esta bindeado.
//
// Desde la v1.2.0 **no escribe `governmentid` ni `axx_fiscalstate`**, ni lo
// intenta. Los dos existen en contact y account pero **no en lead**, donde el
// control tambien esta puesto: ahi cada validacion terminaba en un warning por
// campos que en esa entidad no van a existir. `governmentid` ademas es un campo
// de Microsoft, asi que en lead no se puede crear con el mismo nombre.
//
// `axx_fiscalstate` queda con **un solo escritor**, `SetRucValidationService`,
// que corre por el path de mensajeria y ya lo mantiene sobre el master. Que lo
// escribieran los dos no sumaba nada: no hay merge ni orden garantizado entre
// el formulario y la cola, asi que ganaba el ultimo. Por eso el mapeo de estados
// tampoco vive mas aca — era una copia que habia que mantener sincronizada con
// la del servicio, y ahora hay una sola.
//
// El RUC formateado no se pierde: vuelve por el campo al que el control esta
// bindeado (`getOutputs`) — `msdyn_identificationnumber` en contact y account,
// `axx_numerodocumento` en lead.

const FIELD_DNIT_RESPONSE  = "axx_dnitresponse";

/// <summary>
/// Tipo de documento. Existe en el formulario de **lead**, donde
/// `axx_numerodocumento` guarda CI o RUC segun este campo. En contact y account
/// no esta: ahi el campo bindeado es `msdyn_identificationnumber`, que siempre
/// es un RUC, y la validacion corre como siempre.
/// </summary>
const FIELD_DOCUMENT_TYPE  = "axx_tipodedocumento";

// ── Endpoint ─────────────────────────────────────────────────────────────────

/// <summary>
/// Environment variable con la URL completa de la consulta, **key incluida**:
///
///   https://fa-axxonfiscal-inte.azurewebsites.net/api/set/consulta-ruc?code=…
///
/// El control le agrega `&ruc=..&dv=..`.
///
/// Mismo criterio que `axx_FUNCTION_URL` de
/// [TicketAtencion](docs/wiki/integraciones/ticketatencion.md): la key va dentro
/// de la URL y no en un header aparte, para tener **un solo lugar** que tocar
/// cuando rota o cuando cambia el ambiente. Antes vivia en los parametros del
/// control, replicada en 15 lugares —5 formularios x 3 form factors— y se perdia
/// con cualquier import de solucion.
///
/// La key llega al browser igual: la variable no la esconde, solo la centraliza.
/// </summary>
const ENV_VAR_CONSULTA_RUC_URL = "axx_FISCAL_CONSULTA_RUC_URL";

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

    /// <summary>
    /// La environment variable se lee una vez por sesion del browser y no por
    /// instancia: un formulario puede tener el control mas de una vez, y la URL
    /// es la misma para todas. Es una promesa y no un string para que dos
    /// instancias que arrancan a la par compartan la misma query.
    /// </summary>
    private static _urlCache: Promise<string | null> | null = null;

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
        const entered = this._input.value?.trim();
        if (!entered) {
            this._showStatus("error", "Ingrese un RUC antes de validar.");
            return;
        }

        // Si el formulario dice que el documento no es un RUC, no hay nada que
        // consultar: la SET solo conoce RUCs. Mandarle una cedula devolveria
        // "no registrado", que se lee como un dato malo cuando en realidad la
        // consulta nunca correspondia.
        const tipoDocumento = this._tipoDocumento();
        if (tipoDocumento && !tipoDocumento.toUpperCase().includes("RUC")) {
            this._showStatus("info",
                `El documento es ${tipoDocumento}, no un RUC: no se valida contra la SET.`);
            return;
        }

        // La SET pide el RUC y el digito verificador por separado; el campo del
        // formulario los guarda juntos ("80012345-0"). Se parte aca y no en la
        // Function porque es el mismo criterio que usa SetRucValidationService.
        const partes = this._splitRuc(entered);
        if (!partes) {
            this._showStatus("error",
                "Ingrese el RUC con dígito verificador, separado por guión (ej: 80012345-0).");
            return;
        }

        this._setLoading(true);

        try {
            const body = await this._callApi(partes.ruc, partes.dv);
            const contribuyente = body.contribuyente;

            if (!contribuyente?.razonSocial) {
                // La SET contesta 200 aunque el RUC no exista: lo que lo distingue
                // es que no venga contribuyente. El mensaje del organismo, si vino,
                // dice mas que cualquier texto nuestro.
                this._showStatus("error",
                    body.mensaje?.trim() || "RUC no encontrado en la SET.");
                return;
            }

            const estado = this._estadoDe(body);

            // Actualizar campos del formulario via Xrm
            const warnings = this._updateFormFields(contribuyente, body);

            const resumen = `${contribuyente.razonSocial.trim()} — ${estado ?? "sin estado"}`;
            if (warnings.length > 0) {
                // Se encontro el RUC pero algun campo no se pudo actualizar:
                // avisar en vez de mostrar exito enganoso.
                this._showStatus("warning", `${resumen}. ${warnings.join(" ")}`);
            } else {
                this._showStatus("success", resumen);
            }

            // Persistir el valor formateado (ej: "80012345-0")
            this._currentValue = this._rucFormateado(partes);
            this._input.value  = this._currentValue;
            this._notifyOutputChanged();

        } catch (err) {
            const msg = err instanceof Error ? err.message : "Error al conectar con la API.";
            this._showStatus("error", msg);
        } finally {
            this._setLoading(false);
        }
    }

    // ── armado de la URL ─────────────────────────────────────────────────────

    /// <summary>
    /// Arma la URL de la consulta **pisando** `ruc` y `dv` en vez de agregarlos.
    ///
    /// La diferencia no es cosmetica. La environment variable la escribe una
    /// persona, y es facil que quede pegada una URL de prueba con `ruc` y `dv`
    /// adentro; concatenando, la request viaja con el parametro repetido y la
    /// SET los recibe unidos por coma:
    ///
    ///   ruc=3384261,6599526  ->  "3384261,6599526-2,0 no registrado"
    ///
    /// Ese mensaje parece un RUC inexistente y manda a buscar el problema al
    /// lado equivocado. `searchParams.set` reemplaza todas las apariciones, asi
    /// que la URL configurada puede traer parametros de mas sin romper nada.
    /// </summary>
    private _urlConsulta(endpoint: string, ruc: string, dv: string): string {
        let url: URL;
        try {
            url = new URL(endpoint);
        } catch {
            throw new Error(
                `La URL configurada no es valida: "${endpoint}". ` +
                `Revisar ${ENV_VAR_CONSULTA_RUC_URL} o el parametro ApiBaseUrl del control.`);
        }

        url.searchParams.set("ruc", ruc);
        url.searchParams.set("dv", dv);
        return url.toString();
    }

    // ── resolucion del endpoint ──────────────────────────────────────────────

    /// <summary>
    /// URL de la consulta, sin los parametros `ruc`/`dv`.
    ///
    /// Gana la environment variable; si no esta, se cae a los parametros
    /// `ApiBaseUrl` y `ApiKey` del control. El fallback no es decorativo: es lo
    /// que hace que el control siga andando en los formularios que todavia no
    /// migraron y en el harness de `pcf-scripts start`, donde no hay Dataverse.
    /// </summary>
    private async _endpoint(): Promise<string> {
        const desdeVariable = await this._urlDeEnvironmentVariable();
        if (desdeVariable) return desdeVariable;

        const base   = (this._context.parameters.ApiBaseUrl.raw ?? "").replace(/\/$/, "");
        const apiKey = this._context.parameters.ApiKey.raw ?? "";

        if (!base) {
            throw new Error(
                `Falta configurar ${ENV_VAR_CONSULTA_RUC_URL} (o el parametro ApiBaseUrl del control).`);
        }

        const url = `${base}/api/set/consulta-ruc`;
        return apiKey ? `${url}?code=${encodeURIComponent(apiKey)}` : url;
    }

    /// <summary>
    /// Lee la environment variable una sola vez por sesion del browser.
    ///
    /// El valor del ambiente (`environmentvariablevalue`) pisa al `defaultvalue`
    /// de la definicion; si no hay ninguno de los dos, devuelve null y decide el
    /// fallback. Un error de la query tampoco se propaga: el control tiene que
    /// poder seguir con los parametros, no quedarse mudo porque el usuario no
    /// tenga lectura sobre la tabla de variables.
    /// </summary>
    private _urlDeEnvironmentVariable(): Promise<string | null> {
        RucValidatorControl._urlCache ??= this._leerEnvironmentVariable();
        return RucValidatorControl._urlCache;
    }

    private async _leerEnvironmentVariable(): Promise<string | null> {
        const webApi = this._context.webAPI;
        if (!webApi) return null;

        try {
            const query =
                `?$select=defaultvalue` +
                `&$filter=schemaname eq '${ENV_VAR_CONSULTA_RUC_URL}'` +
                `&$expand=environmentvariabledefinition_environmentvariablevalue($select=value)`;

            const res = await webApi.retrieveMultipleRecords(
                "environmentvariabledefinition", query);

            const def = res.entities?.[0] as {
                defaultvalue?: string;
                environmentvariabledefinition_environmentvariablevalue?: { value?: string }[];
            } | undefined;
            if (!def) return null;

            const delAmbiente =
                def.environmentvariabledefinition_environmentvariablevalue?.[0]?.value;

            return (delAmbiente || def.defaultvalue || "").trim() || null;
        } catch {
            // Sin permiso sobre la tabla, o Dataverse caido: que decida el fallback.
            return null;
        }
    }

    // ── tipo de documento del formulario ─────────────────────────────────────

    /// <summary>
    /// Etiqueta del tipo de documento ("RUC", "CI"...), o null si no aplica.
    ///
    /// Devuelve null en dos casos que significan lo mismo para el caller —
    /// "no hay motivo para saltear la validacion":
    ///   - el campo no esta en el formulario (contact, account)
    ///   - esta pero sin valor seleccionado
    ///
    /// Se compara por **etiqueta** y no por el valor del OptionSet porque los
    /// numeros no son estables entre environments; el texto si.
    /// </summary>
    private _tipoDocumento(): string | null {
        const xrm = (window as unknown as { Xrm?: typeof Xrm }).Xrm;
        const formContext = xrm?.Page as Xrm.FormContext | undefined;
        if (!formContext) return null;

        const attr = formContext.getAttribute(FIELD_DOCUMENT_TYPE) as
            Xrm.Attributes.OptionSetAttribute | null;
        if (!attr) return null;

        return attr.getText()?.trim() || null;
    }

    // ── parseo del RUC ───────────────────────────────────────────────────────

    /// <summary>
    /// Parte "80012345-0" en RUC y digito verificador. Devuelve null si no hay
    /// guion: sin DV la SET no puede responder, asi que se corta antes de salir
    /// a la red en vez de mandar una consulta que ya sabemos incompleta.
    /// </summary>
    private _splitRuc(value: string): { ruc: string; dv: string } | null {
        const dashIdx = value.indexOf("-");
        if (dashIdx <= 0) return null;

        const ruc = value.substring(0, dashIdx).trim();
        const dv  = value.substring(dashIdx + 1).trim();

        return ruc && dv ? { ruc, dv } : null;
    }

    /// <summary>
    /// El RUC formateado que se guarda en el formulario.
    ///
    /// La SET **no devuelve el RUC** en la respuesta —verificado contra INTE el
    /// 2026-09-03: `contribuyente` trae razonSocial, estado, tipoPersona,
    /// categoria y poco mas—, asi que el unico valor disponible es el que se
    /// consulto. Se reconstruye desde ahi. Devolver null dejaria `governmentid`
    /// sin escribir, que es lo que hacia TURUC cuando si mandaba el campo.
    /// </summary>
    private _rucFormateado(partes: { ruc: string; dv: string }): string {
        return `${partes.ruc}-${partes.dv}`;
    }

    /// <summary>
    /// El estado fiscal, que vive **solo** dentro de contribuyente.
    ///
    /// No hay fallback al `estado` de arriba a proposito: ese vale "VALIDO" o
    /// "INVALIDO" y dice si la consulta encontro el RUC, no como esta el
    /// contribuyente. Mezclarlos mandaria "VALIDO" al mapeo de axx_fiscalstate,
    /// que no lo tiene, y el warning haria pensar que la SET devolvio un estado
    /// desconocido cuando en realidad nunca devolvio ninguno.
    /// </summary>
    private _estadoDe(body: SetRucResponse): string | undefined {
        return body.contribuyente?.estado?.trim() || undefined;
    }

    // ── llamada a la Azure Function ──────────────────────────────────────────

    /// <summary>
    /// GET /api/set/consulta-ruc?ruc={ruc}&amp;dv={dv} — la consulta oficial de la
    /// DNIT. La function key viaja como `code` en la query y no como header
    /// `x-functions-key` a proposito: un header custom dispara preflight, y con
    /// `code` el navegador manda el GET directo.
    ///
    /// El apiKey de la SET no se pasa desde aca: lo agrega la Function desde el
    /// Key Vault. Este control nunca ve esa credencial.
    /// </summary>
    private async _callApi(ruc: string, dv: string): Promise<SetRucResponse> {
        const url = this._urlConsulta(await this._endpoint(), ruc, dv);

        const response = await fetch(url, {
            method: "GET",
            headers: { "Accept": "application/json" },
        });

        const rawText = await response.text();

        // El status se mira antes de parsear: la Function devuelve {"error":"..."}
        // en los 400, pero un 401 de la plataforma (key vacia o de otro ambiente)
        // viene con el body vacio. Parseando primero, ese caso —el mas comun de
        // todos— se reportaba como "respuesta no es JSON valido" y escondia el 401.
        if (!response.ok) {
            let detalle = "";
            try {
                detalle = (JSON.parse(rawText) as { error?: string }).error?.trim() ?? "";
            } catch { /* la Function no siempre contesta JSON en los errores */ }

            throw new Error(detalle
                ? `${detalle} (HTTP ${response.status})`
                : `HTTP ${response.status} al consultar la API de la SET.`);
        }

        try {
            return JSON.parse(rawText) as SetRucResponse;
        } catch {
            throw new Error(`Respuesta no es JSON válido: ${rawText.substring(0, 100)}`);
        }
    }

    // ── actualizar campos Dataverse via Xrm ─────────────────────────────────

    /// <summary>
    /// Escribe los campos del contribuyente en el formulario y devuelve la lista
    /// de warnings (vacia si todo se actualizo). Los casos que antes fallaban en
    /// silencio (sin Xrm/form context, estado no reconocido) ahora se reportan.
    ///
    /// Nota: se usa el Xrm global (Xrm.Page) porque un PCF standard control no
    /// tiene una API soportada para escribir columnas hermanas del formulario.
    /// Es el workaround aceptado; funciona en forms model-driven.
    /// </summary>
    private _updateFormFields(c: SetContribuyente, body: SetRucResponse): string[] {
        const warnings: string[] = [];

        const xrm = (window as unknown as { Xrm?: typeof Xrm }).Xrm;
        const formContext = xrm?.Page as Xrm.FormContext | undefined;
        if (!formContext) {
            warnings.push("No se pudo acceder al formulario para actualizar los campos.");
            return warnings;
        }

        // axx_dnitresponse = la respuesta completa de la SET, tal como la deja
        // SetRucValidationService desde el path de mensajeria. Es el campo que
        // renderiza el DnitResponseViewer: guardar el JSON en otro lado lo
        // dejaria invisible para ese control.
        //
        // Es lo unico que el control escribe fuera del campo al que esta bindeado.
        // No toca `governmentid` ni `axx_fiscalstate`: ver la nota de la clase.
        this._setTextField(
            formContext, FIELD_DNIT_RESPONSE, JSON.stringify(body, null, 2), warnings);

        // ── Nombres de persona fisica ────────────────────────────────────────
        //
        // Solo se tocan si la SET dice que el RUC es de una persona fisica: partir
        // la razon social de una empresa en apellido y nombres deja el contacto con
        // datos falsos que nadie nota.
        //
        // Pero saber que es fisica no alcanza para partirla. La SET devuelve el
        // nombre **sin separador y con los nombres primero**:
        //
        //   SET:   "JORGE SEBASTIAN MIRANDA RUIZ DIAZ"
        //   TURUC: "MIRANDA RUIZ DIAZ, JORGE SEBASTIAN"   (la coma marcaba el corte)
        //
        // Sin la coma no hay forma de saber donde terminan los nombres y empiezan
        // los apellidos: "JORGE SEBASTIAN MIRANDA" podria ser dos nombres y un
        // apellido, o un nombre y dos apellidos. Por eso se parte solo si viene la
        // coma, y si no, no se escribe nada y se avisa con el nombre completo para
        // que se cargue a mano.
        const razonSocial = c.razonSocial?.trim();
        if (razonSocial) {
            const esFisica = this._esPersonaFisica(c.tipoPersona);

            if (esFisica === null) {
                warnings.push(c.tipoPersona
                    ? `Tipo de persona "${c.tipoPersona}" no reconocido; no se actualizaron los nombres.`
                    : "La SET no informo el tipo de persona; no se actualizaron los nombres.");
            } else if (esFisica && razonSocial.includes(",")) {
                const { lastname, firstname, middlename } = this._parseRazonSocial(razonSocial);
                this._setTextField(formContext, "lastname",   lastname,   warnings);
                this._setTextField(formContext, "firstname",  firstname,  warnings);
                this._setTextField(formContext, "middlename", middlename, warnings);
            } else if (esFisica) {
                warnings.push(
                    `La SET devolvio el nombre sin separar apellidos: "${razonSocial}". ` +
                    "Cargar nombre y apellido a mano.");
            }
        }

        return warnings;
    }

    // ── tipo de contribuyente ────────────────────────────────────────────────

    /// <summary>
    /// true = persona fisica, false = juridica, null = no se pudo determinar.
    ///
    /// La SET no trae los flags `esPersonaJuridica`/`esEntidadPublica` que traia
    /// TURUC: el equivalente es `tipoPersona`, que vale **"FISICO"** o
    /// **"JURIDICO"** (verificado contra INTE el 2026-09-03).
    ///
    /// El match es por la raiz FISIC/JURIDIC y no por el valor completo para no
    /// romperse con la terminacion: la SET usa el masculino ("JURIDICO") aunque
    /// el termino de negocio sea "persona juridica". Lo que no cae en ninguno de
    /// los dos lados devuelve null en vez de asumir.
    /// </summary>
    private _esPersonaFisica(tipoPersona: string | undefined): boolean | null {
        if (!tipoPersona) return null;

        const normalizado = tipoPersona
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")   // saca los acentos antes de comparar
            .toUpperCase();

        if (normalizado.includes("JURIDIC")) return false;
        if (normalizado.includes("FISIC"))   return true;

        return null;
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

    /// <summary>
    /// Escribe un campo de texto y acumula un warning si no esta en el formulario.
    /// Antes ese caso volvia en silencio: el control mostraba exito y el dato no
    /// se guardaba en ningun lado. Importa mas ahora que la respuesta va a
    /// axx_dnitresponse, que no todos los formularios tienen puesto.
    /// </summary>
    private _setTextField(
        formContext: Xrm.FormContext,
        fieldName: string,
        value: string | null | undefined,
        warnings: string[]
    ): void {
        if (!value) return;
        const attr = formContext.getAttribute(fieldName) as
            Xrm.Attributes.StringAttribute | null;
        if (!attr) {
            warnings.push(`El campo ${fieldName} no esta en el formulario.`);
            return;
        }
        attr.setValue(value);
        attr.fireOnChange();
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

    private _showStatus(
        type: "success" | "error" | "warning" | "info",
        message: string
    ): void {
        this._statusRow.className  = `ruc-status ruc-status--${type}`;
        this._statusIcon.textContent = type === "success" ? "✅" :
                                       type === "warning" ? "⚠️" :
                                       type === "info"    ? "ℹ️" : "❌";
        this._statusText.textContent = message;
    }

    private _clearStatus(): void {
        this._statusRow.className    = "ruc-status hidden";
        this._statusText.textContent = "";
    }
}
