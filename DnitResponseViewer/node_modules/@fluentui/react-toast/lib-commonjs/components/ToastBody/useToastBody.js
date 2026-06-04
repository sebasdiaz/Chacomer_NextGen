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
    useToastBodyBase_unstable: function() {
        return useToastBodyBase_unstable;
    },
    useToastBody_unstable: function() {
        return useToastBody_unstable;
    }
});
const _reactutilities = require("@fluentui/react-utilities");
const _toastContainerContext = require("../../contexts/toastContainerContext");
const _reactsharedcontexts = require("@fluentui/react-shared-contexts");
const useToastBodyBase_unstable = (props, ref)=>{
    const { bodyId } = (0, _toastContainerContext.useToastContainerContext)();
    return {
        components: {
            root: 'div',
            subtitle: 'div'
        },
        subtitle: _reactutilities.slot.optional(props.subtitle, {
            elementType: 'div'
        }),
        root: _reactutilities.slot.always((0, _reactutilities.getIntrinsicElementProps)('div', {
            // FIXME:
            // `ref` is wrongly assigned to be `HTMLElement` instead of `HTMLDivElement`
            // but since it would be a breaking change to fix it, we are casting ref to it's proper type
            ref: ref,
            id: bodyId,
            ...props
        }), {
            elementType: 'div'
        })
    };
};
const useToastBody_unstable = (props, ref)=>{
    const backgroundAppearance = (0, _reactsharedcontexts.useBackgroundAppearance)();
    return {
        ...useToastBodyBase_unstable(props, ref),
        backgroundAppearance
    };
};
