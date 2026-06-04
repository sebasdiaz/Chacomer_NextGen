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
    useAppItemStaticBase_unstable: function() {
        return useAppItemStaticBase_unstable;
    },
    useAppItemStatic_unstable: function() {
        return useAppItemStatic_unstable;
    }
});
const _reactutilities = require("@fluentui/react-utilities");
const _NavContext = require("../NavContext");
const useAppItemStatic_unstable = (props, ref)=>{
    const { density = 'medium' } = (0, _NavContext.useNavContext_unstable)();
    const state = useAppItemStaticBase_unstable(props, ref);
    return {
        ...state,
        density
    };
};
const useAppItemStaticBase_unstable = (props, ref)=>{
    const { icon } = props;
    return {
        components: {
            root: 'div',
            icon: 'span'
        },
        root: _reactutilities.slot.always((0, _reactutilities.getIntrinsicElementProps)('div', {
            ref,
            ...props
        }), {
            elementType: 'div'
        }),
        icon: _reactutilities.slot.optional(icon, {
            elementType: 'span'
        })
    };
};
