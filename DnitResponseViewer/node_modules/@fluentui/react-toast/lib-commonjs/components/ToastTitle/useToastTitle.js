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
    useToastTitleBase_unstable: function() {
        return useToastTitleBase_unstable;
    },
    useToastTitle_unstable: function() {
        return useToastTitle_unstable;
    }
});
const _interop_require_wildcard = require("@swc/helpers/_/_interop_require_wildcard");
const _react = /*#__PURE__*/ _interop_require_wildcard._(require("react"));
const _reacticons = require("@fluentui/react-icons");
const _reactutilities = require("@fluentui/react-utilities");
const _reactsharedcontexts = require("@fluentui/react-shared-contexts");
const _toastContainerContext = require("../../contexts/toastContainerContext");
const useToastTitleBase_unstable = (props, ref)=>{
    const { intent, titleId } = (0, _toastContainerContext.useToastContainerContext)();
    return {
        action: _reactutilities.slot.optional(props.action, {
            elementType: 'div'
        }),
        components: {
            root: 'div',
            media: 'div',
            action: 'div'
        },
        media: _reactutilities.slot.optional(props.media, {
            renderByDefault: !!intent,
            elementType: 'div'
        }),
        root: _reactutilities.slot.always((0, _reactutilities.getIntrinsicElementProps)('div', {
            // FIXME:
            // `ref` is wrongly assigned to be `HTMLElement` instead of `HTMLDivElement`
            // but since it would be a breaking change to fix it, we are casting ref to it's proper type
            ref: ref,
            children: props.children,
            id: titleId,
            ...props
        }), {
            elementType: 'div'
        }),
        intent
    };
};
const useToastTitle_unstable = (props, ref)=>{
    'use no memo';
    const backgroundAppearance = (0, _reactsharedcontexts.useBackgroundAppearance)();
    const baseState = useToastTitleBase_unstable(props, ref);
    /** Determine the role and media to render based on the intent */ let defaultIcon;
    switch(baseState.intent){
        case 'success':
            defaultIcon = /*#__PURE__*/ _react.createElement(_reacticons.CheckmarkCircleFilled, null);
            break;
        case 'error':
            defaultIcon = /*#__PURE__*/ _react.createElement(_reacticons.DiamondDismissFilled, null);
            break;
        case 'warning':
            defaultIcon = /*#__PURE__*/ _react.createElement(_reacticons.WarningFilled, null);
            break;
        case 'info':
            defaultIcon = /*#__PURE__*/ _react.createElement(_reacticons.InfoFilled, null);
            break;
    }
    return {
        ...baseState,
        media: _reactutilities.slot.optional(props.media, {
            defaultProps: {
                children: defaultIcon
            },
            renderByDefault: !!baseState.intent,
            elementType: 'div'
        }),
        backgroundAppearance
    };
};
