import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { ChildContactsGrid, IChildContact } from "./ChildContactsGrid";
import * as React from "react";

const CONTACT_ENTITY = "contact";
const SELECT_FIELDS = "contactid,fullname,_msdyn_company_value,_msdyn_customergroupid_value";
const EXPAND_FIELDS = "msdyn_company($select=cdm_name),msdyn_customergroupid($select=msdyn_description)";

interface IContactEntity {
    contactid: string;
    fullname: string;
    msdyn_company?: { cdm_name?: string };
    msdyn_customergroupid?: { msdyn_description?: string };
}

export class MasterContactChildrenGrid implements ComponentFramework.ReactControl<IInputs, IOutputs> {
    private notifyOutputChanged: () => void;
    private context: ComponentFramework.Context<IInputs>;

    private contacts: IChildContact[] = [];
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
            void this.loadChildContacts(masterContactId);
        } else if (!masterContactId && this.contacts.length > 0) {
            this.contacts = [];
            this.lastMasterContactId = null;
        }

        return React.createElement(ChildContactsGrid, {
            contacts: this.contacts,
            isLoading: this.isLoading,
            errorMessage: this.errorMessage,
        });
    }

    private async loadChildContacts(masterContactId: string): Promise<void> {
        this.isLoading = true;
        this.errorMessage = null;
        this.notifyOutputChanged();

        const options = `?$select=${SELECT_FIELDS}&$expand=${EXPAND_FIELDS}&$filter=_axx_mastercontactid_value eq '${masterContactId}'`;

        try {
            const result = await this.context.webAPI.retrieveMultipleRecords(CONTACT_ENTITY, options);
            this.contacts = (result.entities as unknown as IContactEntity[]).map((e) => ({
                contactid: e.contactid,
                fullname: e.fullname ?? "",
                legalEntityName: e.msdyn_company?.cdm_name ?? "",
                customerGroupName: e.msdyn_customergroupid?.msdyn_description ?? "",
            }));
            this.isLoading = false;
            this.notifyOutputChanged();
        } catch (error) {
            this.errorMessage = `Error loading contacts: ${(error as Error).message}`;
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
