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
    useTeachingPopoverTitleBase_unstable: function() {
        return useTeachingPopoverTitleBase_unstable;
    },
    useTeachingPopoverTitle_unstable: function() {
        return useTeachingPopoverTitle_unstable;
    }
});
const _interop_require_wildcard = require("@swc/helpers/_/_interop_require_wildcard");
const _react = /*#__PURE__*/ _interop_require_wildcard._(require("react"));
const _reactutilities = require("@fluentui/react-utilities");
const _reacticons = require("@fluentui/react-icons");
const _reactpopover = require("@fluentui/react-popover");
const DismissIcon = (0, _reacticons.bundleIcon)(_reacticons.DismissFilled, _reacticons.DismissRegular);
const useTeachingPopoverTitleBase_unstable = (props, ref)=>{
    const { dismissButton } = props;
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
            root: 'h2',
            dismissButton: 'button'
        },
        root: _reactutilities.slot.always((0, _reactutilities.getIntrinsicElementProps)('h2', {
            ref,
            ...props
        }), {
            elementType: 'h2'
        }),
        dismissButton: _reactutilities.slot.optional(dismissButton, {
            renderByDefault: false,
            defaultProps: {
                onClick: onDismissButtonClick,
                'aria-label': 'dismiss',
                'aria-hidden': true
            },
            elementType: 'button'
        })
    };
};
const useTeachingPopoverTitle_unstable = (props, ref)=>{
    const baseState = useTeachingPopoverTitleBase_unstable(props, ref);
    const appearance = (0, _reactpopover.usePopoverContext_unstable)((context)=>context.appearance);
    const dismissButton = baseState.dismissButton && baseState.dismissButton.children === undefined ? {
        ...baseState.dismissButton,
        children: /*#__PURE__*/ _react.createElement(DismissIcon, null)
    } : baseState.dismissButton;
    return {
        ...baseState,
        appearance,
        dismissButton
    };
};
