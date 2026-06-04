'use client';
"use strict";
Object.defineProperty(exports, "__esModule", {
    value: true
});
function _export(target, all) {
    for(var name in all)Object.defineProperty(target, name, {
        enumerable: true,
        get: all[name]
    });
}
_export(exports, {
    useTeachingPopoverFooterBase_unstable: function() {
        return useTeachingPopoverFooterBase_unstable;
    },
    useTeachingPopoverFooter_unstable: function() {
        return useTeachingPopoverFooter_unstable;
    }
});
const _reactutilities = require("@fluentui/react-utilities");
const _reactbutton = require("@fluentui/react-button");
const _reactpopover = require("@fluentui/react-popover");
const useTeachingPopoverFooterBase_unstable = (props, ref)=>{
    const toggleOpen = (0, _reactpopover.usePopoverContext_unstable)((context)=>context.toggleOpen);
    const handleButtonClick = (0, _reactutilities.useEventCallback)((event)=>{
        if (event.isDefaultPrevented()) {
            return;
        }
        toggleOpen(event);
    });
    var _props_footerLayout;
    return {
        footerLayout: (_props_footerLayout = props.footerLayout) !== null && _props_footerLayout !== void 0 ? _props_footerLayout : 'horizontal',
        handleButtonClick,
        components: {
            root: 'div'
        },
        root: _reactutilities.slot.always((0, _reactutilities.getIntrinsicElementProps)('div', {
            ref,
            ...props
        }), {
            elementType: 'div'
        })
    };
};
const useTeachingPopoverFooter_unstable = (props, ref)=>{
    const baseState = useTeachingPopoverFooterBase_unstable(props, ref);
    const appearance = (0, _reactpopover.usePopoverContext_unstable)((context)=>context.appearance);
    const isBrand = appearance === 'brand';
    const primaryDefaultAppearance = isBrand ? undefined : 'primary';
    const secondaryDefaultAppearance = isBrand ? 'primary' : undefined;
    const secondary = _reactutilities.slot.optional(props.secondary, {
        defaultProps: {
            appearance: secondaryDefaultAppearance
        },
        renderByDefault: props.secondary !== undefined,
        elementType: _reactbutton.Button
    });
    if (secondary) {
        secondary.onClick = (0, _reactutilities.mergeCallbacks)(baseState.handleButtonClick, secondary === null || secondary === void 0 ? void 0 : secondary.onClick);
    }
    const primary = _reactutilities.slot.always(props.primary, {
        defaultProps: {
            appearance: primaryDefaultAppearance
        },
        elementType: _reactbutton.Button
    });
    if (!secondary) {
        primary.onClick = (0, _reactutilities.mergeCallbacks)(baseState.handleButtonClick, primary === null || primary === void 0 ? void 0 : primary.onClick);
    }
    return {
        footerLayout: baseState.footerLayout,
        appearance,
        components: {
            root: 'div',
            primary: _reactbutton.Button,
            secondary: _reactbutton.Button
        },
        root: baseState.root,
        secondary,
        primary
    };
};
