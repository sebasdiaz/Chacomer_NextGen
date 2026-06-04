"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.createFluentIcon = void 0;
const tslib_1 = require("tslib");
const React = tslib_1.__importStar(require("react"));
const shared_1 = require("./shared");
const useIconState_1 = require("./useIconState");
const renderSvgNode = (node, key) => {
    const [tag, attrs, ...children] = node;
    return React.createElement(tag, { ...attrs, key }, ...children.map(renderSvgNode));
};
/**
 * Headless createFluentIcon — SVG icon factory without Styles.
 *
 * Returns a React component that renders an SVG icon with:
 * - data-fui-icon attribute for CSS targeting
 * - a11y attributes (aria-hidden, aria-label, role)
 * - RTL flip via data-fui-icon-rtl attribute
 * - HCM forced-color-adjust via CSS attribute selector
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
 *
 * @access private
 * @alpha
 */
const createFluentIcon = (displayName, width, pathsOrSvg, options) => {
    const viewBoxWidth = width === '1em' ? '20' : width;
    // Pre-render color SVG nodes once in the factory so the recursion
    // never runs during React renders.
    const colorChildren = typeof pathsOrSvg !== 'string' && ((options === null || options === void 0 ? void 0 : options.color) || Array.isArray(pathsOrSvg[0]))
        ? pathsOrSvg.map(renderSvgNode)
        : undefined;
    const Icon = React.forwardRef((props, ref) => {
        const iconState = useIconState_1.useIconState(props, { flipInRtl: options === null || options === void 0 ? void 0 : options.flipInRtl });
        const state = {
            ...iconState,
            className: shared_1.cx(shared_1.iconClassName, iconState.className),
            ref,
            width,
            height: width,
            viewBox: `0 0 ${viewBoxWidth} ${viewBoxWidth}`,
            xmlns: 'http://www.w3.org/2000/svg',
        };
        if (typeof pathsOrSvg === 'string') {
            return React.createElement('svg', { ...state, dangerouslySetInnerHTML: { __html: pathsOrSvg } });
        }
        if (colorChildren) {
            return React.createElement('svg', state, ...colorChildren);
        }
        return React.createElement('svg', state, ...pathsOrSvg.map((d) => React.createElement('path', { d, fill: state.fill })));
    });
    Icon.displayName = displayName;
    return Icon;
};
exports.createFluentIcon = createFluentIcon;
