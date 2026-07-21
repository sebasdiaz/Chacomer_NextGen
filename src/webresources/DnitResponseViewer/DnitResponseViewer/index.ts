import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { DnitResponseCard, IDnitResponseViewerProps } from "./DnitResponseViewer";
import * as React from "react";

export class DnitResponseViewer implements ComponentFramework.ReactControl<IInputs, IOutputs> {
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    constructor() {}

    public init(
        _context: ComponentFramework.Context<IInputs>,
        _notifyOutputChanged: () => void,
        _state: ComponentFramework.Dictionary
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    ): void {}

    public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
        const raw = context.parameters.dnitResponse.raw ?? "";
        const props: IDnitResponseViewerProps = { rawJson: raw };
        return React.createElement(DnitResponseCard, props);
    }

    public getOutputs(): IOutputs {
        return {};
    }

    // eslint-disable-next-line @typescript-eslint/no-empty-function
    public destroy(): void {}
}
