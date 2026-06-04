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
    useNavSubItemGroupBase_unstable: function() {
        return useNavSubItemGroupBase_unstable;
    },
    useNavSubItemGroup_unstable: function() {
        return useNavSubItemGroup_unstable;
    }
});
const _reactutilities = require("@fluentui/react-utilities");
const _reactmotion = require("@fluentui/react-motion");
const _reactmotioncomponentspreview = require("@fluentui/react-motion-components-preview");
const _NavCategoryContext = require("../NavCategoryContext");
const useNavSubItemGroup_unstable = (props, ref)=>{
    const state = useNavSubItemGroupBase_unstable(props, ref);
    return {
        ...state,
        components: {
            // eslint-disable-next-line @typescript-eslint/no-deprecated
            ...state.components,
            collapseMotion: _reactmotioncomponentspreview.Collapse
        },
        collapseMotion: (0, _reactmotion.presenceMotionSlot)(props.collapseMotion, {
            elementType: _reactmotioncomponentspreview.Collapse,
            defaultProps: {
                visible: state.open,
                unmountOnExit: true
            }
        })
    };
};
const useNavSubItemGroupBase_unstable = (props, ref)=>{
    const { open } = (0, _NavCategoryContext.useNavCategoryContext_unstable)();
    return {
        open,
        components: {
            root: 'div'
        },
        root: _reactutilities.slot.always((0, _reactutilities.getIntrinsicElementProps)('div', {
            ...props,
            ref
        }), {
            elementType: 'div'
        })
    };
};
