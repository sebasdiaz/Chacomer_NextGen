import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { DeviceRegistrationGridView } from "./DeviceRegistrationGrid";
import * as React from "react";

export class DeviceRegistrationGrid implements ComponentFramework.ReactControl<IInputs, IOutputs> {
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
        const masterContactId = ((context as unknown as { page?: { entityId?: string } }).page?.entityId) ?? null;

        return React.createElement(DeviceRegistrationGridView, {
            masterContactId,
            webAPI: context.webAPI,
        });
    }

    public getOutputs(): IOutputs {
        return {};
    }

    // eslint-disable-next-line @typescript-eslint/no-empty-function
    public destroy(): void {}
}
