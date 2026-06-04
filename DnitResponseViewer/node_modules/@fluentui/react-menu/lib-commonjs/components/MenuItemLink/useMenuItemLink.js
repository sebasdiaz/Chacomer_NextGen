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
    useMenuItemLinkBase_unstable: function() {
        return useMenuItemLinkBase_unstable;
    },
    useMenuItemLink_unstable: function() {
        return useMenuItemLink_unstable;
    }
});
const _reactutilities = require("@fluentui/react-utilities");
const _useMenuItem = require("../MenuItem/useMenuItem");
const _useMenuItemBase = require("../MenuItem/useMenuItemBase");
const useMenuItemLink_unstable = (props, ref)=>{
    // casting because the root slot changes from div to a
    const baseState = (0, _useMenuItem.useMenuItem_unstable)(props, null);
    // FIXME: casting because the root slot changes from div to a,
    // ideal solution would be to extract common logic from useMenuItem_unstable root
    // and use it in both without assuming element type
    const _props = {
        ...props,
        ...baseState.root,
        ref,
        tabIndex: props.tabIndex
    };
    return {
        ...baseState,
        components: {
            // eslint-disable-next-line @typescript-eslint/no-deprecated
            ...baseState.components,
            root: 'a'
        },
        root: _reactutilities.slot.always((0, _reactutilities.getIntrinsicElementProps)('a', {
            role: 'menuitem',
            ..._props
        }), {
            elementType: 'a'
        })
    };
};
const useMenuItemLinkBase_unstable = (props, ref)=>{
    const baseState = (0, _useMenuItemBase.useMenuItemBase_unstable)(props, null);
    const _props = {
        ...props,
        ...baseState.root,
        ref,
        tabIndex: props.tabIndex
    };
    return {
        ...baseState,
        components: {
            // eslint-disable-next-line @typescript-eslint/no-deprecated
            ...baseState.components,
            root: 'a'
        },
        root: _reactutilities.slot.always((0, _reactutilities.getIntrinsicElementProps)('a', {
            role: 'menuitem',
            ..._props
        }), {
            elementType: 'a'
        })
    };
};
