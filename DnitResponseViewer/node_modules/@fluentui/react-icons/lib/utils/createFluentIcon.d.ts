import * as React from 'react';
import { FluentIconsProps } from './FluentIconsProps.types';
export declare type FluentIcon = React.FC<FluentIconsProps>;
export declare type CreateFluentIconOptions = {
    flipInRtl?: boolean;
    color?: boolean;
};
export declare type SvgNode = [
    tag: string,
    attrs: Record<string, string | Record<string, string>> | null,
    ...children: SvgNode[]
];
/**
 * Creates a Fluent icon React component with Griffel styling.
 *
 * @param displayName - The display name for the component (used in React DevTools).
 * @param width - The intrinsic width/height of the icon (e.g. `"20"`, `"24"`, `"1em"`).
 * @param pathsOrSvg - Icon content in one of three forms:
 *   - `string[]` — Array of SVG path `d` attributes (mono-color icons).
 *   - `SvgNode[]` — Structured SVG element tree for color icons (CSP-safe).
 *   - `string` — Raw SVG innerHTML string.
 *     **Deprecated:** Use `SvgNode[]` with `options.color` instead. The `string` overload uses
 *     `dangerouslySetInnerHTML` which violates Trusted Types CSP policies.
 * @param options - Optional configuration.
 */
export declare const createFluentIcon: (displayName: string, width: string, pathsOrSvg: string[] | string | SvgNode[], options?: CreateFluentIconOptions | undefined) => FluentIcon;
