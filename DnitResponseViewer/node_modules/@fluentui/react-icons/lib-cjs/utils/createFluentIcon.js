"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.createFluentIcon = void 0;
const tslib_1 = require("tslib");
const React = tslib_1.__importStar(require("react"));
const react_1 = require("@griffel/react");
const useIconState_1 = require("./useIconState");
const createFluentIcon_styles_1 = require("./createFluentIcon.styles");
const constants_1 = require("./constants");
const renderSvgNode = (node, key) => {
    const [tag, attrs, ...children] = node;
    return React.createElement(tag, { ...attrs, key }, ...children.map(renderSvgNode));
};
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
const createFluentIcon = (displayName, width, pathsOrSvg, options) => {
    const viewBoxWidth = width === '1em' ? '20' : width;
    // Pre-render color SVG nodes once in the factory so the recursion
    // never runs during React renders.
    const colorChildren = typeof pathsOrSvg !== 'string' && ((options === null || options === void 0 ? void 0 : options.color) || Array.isArray(pathsOrSvg[0]))
        ? pathsOrSvg.map(renderSvgNode)
        : undefined;
    const Icon = React.forwardRef((props, ref) => {
        const styles = createFluentIcon_styles_1.useRootStyles();
        const iconState = useIconState_1.useIconState(props, { flipInRtl: options === null || options === void 0 ? void 0 : options.flipInRtl });
        const state = {
            ...iconState,
            className: react_1.mergeClasses(constants_1.iconClassName, iconState.className, styles.root),
            ref,
            width,
            height: width,
            viewBox: `0 0 ${viewBoxWidth} ${viewBoxWidth}`,
            xmlns: 'http://www.w3.org/2000/svg',
        };
        // @deprecated - this branch is not used in our code, only keeping for backwards compatibility
        // Color icon: render raw SVG children
        if (typeof pathsOrSvg === 'string') {
            return React.createElement('svg', { ...state, dangerouslySetInnerHTML: { __html: pathsOrSvg } });
        }
        // Color icon: use pre-rendered elements
        if (colorChildren) {
            return React.createElement('svg', state, ...colorChildren);
        }
        return React.createElement('svg', state, ...pathsOrSvg.map((d) => React.createElement('path', { d, fill: state.fill })));
    });
    Icon.displayName = displayName;
    return Icon;
};
exports.createFluentIcon = createFluentIcon;
