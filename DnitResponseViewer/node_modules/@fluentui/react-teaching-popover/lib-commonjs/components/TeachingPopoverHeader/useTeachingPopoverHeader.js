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
    useTeachingPopoverHeaderBase_unstable: function() {
        return useTeachingPopoverHeaderBase_unstable;
    },
    useTeachingPopoverHeader_unstable: function() {
        return useTeachingPopoverHeader_unstable;
    }
});
const _interop_require_wildcard = require("@swc/helpers/_/_interop_require_wildcard");
const _react = /*#__PURE__*/ _interop_require_wildcard._(require("react"));
const _reactutilities = require("@fluentui/react-utilities");
const _reacticons = require("@fluentui/react-icons");
const _reactpopover = require("@fluentui/react-popover");
const useTeachingPopoverHeaderBase_unstable = (props, ref)=>{
    const { dismissButton, icon } = props;
    const setOpen = (0, _reactpopover.usePopoverContext_unstable)((context)=>context.setOpen);
    const triggerRef = (0, _reactpopover.usePopoverContext_unstable)((context)=>context.triggerRef);
    const onDismissButtonClick = (0, _reactutilities.useEventCallback)((ev)=>{
        if (!ev.defaultPrevented) {
            setOpen(ev, false);
        }
        if (triggerRef.current) {
            triggerRef.current.focus();
        }
    });
    return {
        components: {
            root: 'div',
            dismissButton: 'button',
            icon: 'div'
        },
        root: _reactutilities.slot.always((0, _reactutilities.getIntrinsicElementProps)('div', {
            ref,
            ...props
        }), {
            elementType: 'div'
        }),
        icon: _reactutilities.slot.optional(icon, {
            renderByDefault: true,
            defaultProps: {
                'aria-hidden': true
            },
            elementType: 'div'
        }),
        dismissButton: _reactutilities.slot.optional(dismissButton, {
            renderByDefault: true,
            defaultProps: {
                role: 'button',
                'aria-label': 'dismiss',
                onClick: onDismissButtonClick
            },
            elementType: 'button'
        })
    };
};
const useTeachingPopoverHeader_unstable = (props, ref)=>{
    const baseState = useTeachingPopoverHeaderBase_unstable(props, ref);
    const appearance = (0, _reactpopover.usePopoverContext_unstable)((context)=>context.appearance);
    const icon = baseState.icon && baseState.icon.children === undefined ? {
        ...baseState.icon,
        children: /*#__PURE__*/ _react.createElement(_reacticons.Lightbulb16Regular, null)
    } : baseState.icon;
    const dismissButton = baseState.dismissButton && baseState.dismissButton.children === undefined ? {
        ...baseState.dismissButton,
        children: /*#__PURE__*/ _react.createElement(_reacticons.Dismiss12Regular, null)
    } : baseState.dismissButton;
    return {
        ...baseState,
        appearance,
        icon,
        dismissButton
    };
};
