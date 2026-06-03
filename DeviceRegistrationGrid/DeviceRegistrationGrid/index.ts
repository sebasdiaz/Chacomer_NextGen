import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { DeviceRegistrationGridView, IDeviceRegistration } from "./DeviceRegistrationGrid";
import * as React from "react";

const CHILD_CONTACT_FILTER = (masterContactId: string) =>
    `?$select=contactid&$filter=_axx_mastercontactid_value eq '${masterContactId}'`;

const DEVICE_ENTITY = "msauto_deviceregistration";

const DEVICE_SELECT = "msauto_deviceregistrationid,a365_company,_msauto_customerid_value";

const DEVICE_EXPAND = [
    "msauto_deviceid(",
        "$select=msauto_devicenumber,msauto_name,msauto_description;",
        "$expand=",
            "msauto_devicebrandid($select=msauto_name),",
            "msauto_deviceclassid($select=msauto_name),",
            "msauto_devicemodelid($select=msauto_name),",
            "msauto_devicemodelcodeid($select=msauto_name),",
            "msauto_configurationcodeid($select=msauto_name),",
            "a365_deviceexteriorid($select=a365_name),",
            "a365_deviceinteriorid($select=a365_name)",
    ")",
].join("");

interface IChildContact {
    contactid: string;
}

interface IDeviceEntity {
    msauto_deviceregistrationid: string;
    a365_company?: string;
    msauto_deviceid?: {
        msauto_devicenumber?: string;
        msauto_name?: string;
        msauto_description?: string;
        msauto_devicebrandid?: { msauto_name?: string };
        msauto_deviceclassid?: { msauto_name?: string };
        msauto_devicemodelid?: { msauto_name?: string };
        msauto_devicemodelcodeid?: { msauto_name?: string };
        msauto_configurationcodeid?: { msauto_name?: string };
        a365_deviceexteriorid?: { a365_name?: string };
        a365_deviceinteriorid?: { a365_name?: string };
    };
}

export class DeviceRegistrationGrid implements ComponentFramework.ReactControl<IInputs, IOutputs> {
    private notifyOutputChanged: () => void;
    private context: ComponentFramework.Context<IInputs>;

    private items: IDeviceRegistration[] = [];
    private isLoading = false;
    private errorMessage: string | null = null;
    private lastMasterContactId: string | null = null;

    // eslint-disable-next-line @typescript-eslint/no-empty-function
    constructor() {}

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        _state: ComponentFramework.Dictionary
    ): void {
        this.notifyOutputChanged = notifyOutputChanged;
        this.context = context;
    }

    public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
        this.context = context;

        const masterContactId = ((context as unknown as { page?: { entityId?: string } }).page?.entityId) ?? null;

        if (masterContactId && masterContactId !== this.lastMasterContactId) {
            this.lastMasterContactId = masterContactId;
            void this.loadDeviceRegistrations(masterContactId);
        } else if (!masterContactId && this.items.length > 0) {
            this.items = [];
            this.lastMasterContactId = null;
        }

        return React.createElement(DeviceRegistrationGridView, {
            items: this.items,
            isLoading: this.isLoading,
            errorMessage: this.errorMessage,
        });
    }

    private async loadDeviceRegistrations(masterContactId: string): Promise<void> {
        this.isLoading = true;
        this.errorMessage = null;
        this.notifyOutputChanged();

        try {
            // Step 1: get child contact IDs linked to the master
            const childResult = await this.context.webAPI.retrieveMultipleRecords(
                "contact",
                CHILD_CONTACT_FILTER(masterContactId)
            );
            const childIds = (childResult.entities as unknown as IChildContact[]).map((c) => c.contactid);

            if (childIds.length === 0) {
                this.items = [];
                this.isLoading = false;
                this.notifyOutputChanged();
                return;
            }

            // Step 2: get device registrations where msauto_customerid is one of the child contacts
            // Dataverse Web API does not support the `in` operator on _value lookup fields; use OR conditions instead
            const orFilter = childIds.map((id) => `_msauto_customerid_value eq '${id}'`).join(" or ");
            const deviceOptions = `?$select=${DEVICE_SELECT}&$expand=${DEVICE_EXPAND}&$filter=(${orFilter})`;

            const deviceResult = await this.context.webAPI.retrieveMultipleRecords(DEVICE_ENTITY, deviceOptions);
            this.items = (deviceResult.entities as unknown as IDeviceEntity[]).map((e) => ({
                msauto_deviceregistrationid: e.msauto_deviceregistrationid,
                companyName:      e.a365_company ?? "",
                deviceNumber:     e.msauto_deviceid?.msauto_devicenumber ?? "",
                deviceName:       e.msauto_deviceid?.msauto_name ?? "",
                deviceDescription: e.msauto_deviceid?.msauto_description ?? "",
                brandName:        e.msauto_deviceid?.msauto_devicebrandid?.msauto_name ?? "",
                className:        e.msauto_deviceid?.msauto_deviceclassid?.msauto_name ?? "",
                modelName:        e.msauto_deviceid?.msauto_devicemodelid?.msauto_name ?? "",
                modelCodeName:    e.msauto_deviceid?.msauto_devicemodelcodeid?.msauto_name ?? "",
                configCodeName:   e.msauto_deviceid?.msauto_configurationcodeid?.msauto_name ?? "",
                exteriorName:     e.msauto_deviceid?.a365_deviceexteriorid?.a365_name ?? "",
                interiorName:     e.msauto_deviceid?.a365_deviceinteriorid?.a365_name ?? "",
            }));

            this.isLoading = false;
            this.notifyOutputChanged();
        } catch (error) {
            this.errorMessage = `Error loading device registrations: ${(error as Error).message}`;
            this.isLoading = false;
            this.notifyOutputChanged();
        }
    }

    public getOutputs(): IOutputs {
        return {};
    }

    // eslint-disable-next-line @typescript-eslint/no-empty-function
    public destroy(): void {}
}
