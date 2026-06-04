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
    useAppItemBase_unstable: function() {
        return useAppItemBase_unstable;
    },
    useAppItem_unstable: function() {
        return useAppItem_unstable;
    }
});
const _reactutilities = require("@fluentui/react-utilities");
const _reactaria = require("@fluentui/react-aria");
const _NavContext = require("../NavContext");
const useAppItem_unstable = (props, ref)=>{
    const { density = 'medium' } = (0, _NavContext.useNavContext_unstable)();
    const state = useAppItemBase_unstable(props, ref);
    return {
        ...state,
        density
    };
};
const useAppItemBase_unstable = (props, ref)=>{
    const { icon, as, href } = props;
    const rootElementType = as || (href ? 'a' : 'button');
    const root = _reactutilities.slot.always((0, _reactutilities.getIntrinsicElementProps)(rootElementType, (0, _reactaria.useARIAButtonProps)(rootElementType, {
        ...props
    })), {
        elementType: rootElementType,
        defaultProps: {
            ref: ref,
            type: rootElementType
        }
    });
    return {
        components: {
            root: rootElementType,
            icon: 'span'
        },
        root,
        icon: _reactutilities.slot.optional(icon, {
            elementType: 'span'
        })
    };
};
