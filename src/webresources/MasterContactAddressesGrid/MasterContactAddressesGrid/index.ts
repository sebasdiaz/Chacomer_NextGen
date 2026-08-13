import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { MasterAddressesGrid, IMasterAddressesGridProps } from "./MasterAddressesGrid";
import * as React from "react";

export class MasterContactAddressesGrid implements ComponentFramework.ReactControl<IInputs, IOutputs> {
    private notifyOutputChanged: () => void;

    // eslint-disable-next-line @typescript-eslint/no-empty-function
    constructor() {}

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        _state: ComponentFramework.Dictionary
    ): void {
        this.notifyOutputChanged = notifyOutputChanged;
    }

    public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
        // El GUID del master sale de la pagina, no de la propiedad bound: la propiedad
        // existe solo porque PCF exige al menos una. Mismo criterio que MasterContactChildrenGrid.
        const masterContactId = ((context as unknown as { page?: { entityId?: string } }).page?.entityId) ?? null;

        const props: IMasterAddressesGridProps = {
            masterContactId,
            webAPI: context.webAPI,
        };

        return React.createElement(MasterAddressesGrid, props);
    }

    public getOutputs(): IOutputs {
        return {};
    }

    // eslint-disable-next-line @typescript-eslint/no-empty-function
    public destroy(): void {}
}
