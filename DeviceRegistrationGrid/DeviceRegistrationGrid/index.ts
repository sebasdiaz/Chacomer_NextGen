import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { DeviceRegistrationGridView, IDeviceRegistration } from "./DeviceRegistrationGrid";
import * as React from "react";

const CHILD_CONTACT_FILTER = (masterContactId: string) =>
    `?$select=contactid&$filter=_axx_mastercontactid_value eq '${masterContactId}'`;

const DEVICE_ENTITY = "msauto_deviceregistration";
const DEVICE_SELECT = "msauto_deviceregistrationid,msauto_name,msauto_deviceid,_a365_company_value";
const DEVICE_EXPAND = "a365_company($select=cdm_name)";

interface IChildContact {
    contactid: string;
}

interface IDeviceEntity {
    msauto_deviceregistrationid: string;
    msauto_name: string;
    msauto_deviceid: string;
    a365_company?: { cdm_name?: string };
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

            // Step 2: get device registrations where a365_contactid is one of the child contacts
            const inFilter = childIds.map((id) => `'${id}'`).join(",");
            const deviceOptions = `?$select=${DEVICE_SELECT}&$expand=${DEVICE_EXPAND}&$filter=_a365_contactid_value in (${inFilter})`;

            const deviceResult = await this.context.webAPI.retrieveMultipleRecords(DEVICE_ENTITY, deviceOptions);
            this.items = (deviceResult.entities as unknown as IDeviceEntity[]).map((e) => ({
                msauto_deviceregistrationid: e.msauto_deviceregistrationid,
                msauto_name: e.msauto_name ?? "",
                msauto_deviceid: e.msauto_deviceid ?? "",
                companyName: e.a365_company?.cdm_name ?? "",
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
