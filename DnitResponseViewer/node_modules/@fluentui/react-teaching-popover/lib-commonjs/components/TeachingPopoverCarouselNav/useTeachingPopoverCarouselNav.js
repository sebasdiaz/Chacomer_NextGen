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
    useTeachingPopoverCarouselNavBase_unstable: function() {
        return useTeachingPopoverCarouselNavBase_unstable;
    },
    useTeachingPopoverCarouselNav_unstable: function() {
        return useTeachingPopoverCarouselNav_unstable;
    }
});
const _reacttabster = require("@fluentui/react-tabster");
const _reactutilities = require("@fluentui/react-utilities");
const _useCarouselValues = require("../TeachingPopoverCarousel/Carousel/useCarouselValues");
const useTeachingPopoverCarouselNavBase_unstable = (props, ref)=>{
    const values = (0, _useCarouselValues.useCarouselValues_unstable)((snapshot)=>snapshot);
    return {
        values,
        renderNavButton: props.children,
        components: {
            root: 'div'
        },
        root: _reactutilities.slot.always((0, _reactutilities.getIntrinsicElementProps)('div', {
            ref,
            role: 'tablist',
            tabIndex: 0,
            ...props,
            children: null
        }), {
            elementType: 'div'
        })
    };
};
const useTeachingPopoverCarouselNav_unstable = (props, ref)=>{
    const state = useTeachingPopoverCarouselNavBase_unstable(props, ref);
    const focusableGroupAttr = (0, _reacttabster.useArrowNavigationGroup)({
        circular: false,
        axis: 'horizontal',
        memorizeCurrent: false,
        // eslint-disable-next-line @typescript-eslint/naming-convention
        unstable_hasDefault: true
    });
    return {
        ...state,
        root: {
            ...state.root,
            ...focusableGroupAttr
        }
    };
};
