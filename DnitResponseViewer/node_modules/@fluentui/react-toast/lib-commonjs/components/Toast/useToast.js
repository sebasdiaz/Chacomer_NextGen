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
    useToastBase_unstable: function() {
        return useToastBase_unstable;
    },
    useToast_unstable: function() {
        return useToast_unstable;
    }
});
const _reactutilities = require("@fluentui/react-utilities");
const _toastContainerContext = require("../../contexts/toastContainerContext");
const useToastBase_unstable = (props, ref)=>{
    const { intent } = (0, _toastContainerContext.useToastContainerContext)();
    return {
        components: {
            root: 'div'
        },
        root: _reactutilities.slot.always((0, _reactutilities.getIntrinsicElementProps)('div', {
            // FIXME:
            // `ref` is wrongly assigned to be `HTMLElement` instead of `HTMLDivElement`
            // but since it would be a breaking change to fix it, we are casting ref to it's proper type
            ref: ref,
            ...props
        }), {
            elementType: 'div'
        }),
        intent
    };
};
const useToast_unstable = (props, ref)=>{
    const state = useToastBase_unstable(props, ref);
    return {
        ...state,
        backgroundAppearance: props.appearance
    };
};
