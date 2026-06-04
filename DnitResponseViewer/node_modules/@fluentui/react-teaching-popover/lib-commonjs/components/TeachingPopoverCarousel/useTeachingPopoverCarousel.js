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
    useTeachingPopoverCarouselBase_unstable: function() {
        return useTeachingPopoverCarouselBase_unstable;
    },
    useTeachingPopoverCarousel_unstable: function() {
        return useTeachingPopoverCarousel_unstable;
    }
});
const _reactutilities = require("@fluentui/react-utilities");
const _reactpopover = require("@fluentui/react-popover");
const _Carousel = require("./Carousel/Carousel");
const useTeachingPopoverCarouselBase_unstable = (props, ref)=>{
    const toggleOpen = (0, _reactpopover.usePopoverContext_unstable)((c)=>c.toggleOpen);
    const handleFinish = (0, _reactutilities.useEventCallback)((event, data)=>{
        var _props_onFinish;
        (_props_onFinish = props.onFinish) === null || _props_onFinish === void 0 ? void 0 : _props_onFinish.call(props, event, data);
        toggleOpen(event);
    });
    const { carousel, carouselRef } = (0, _Carousel.useCarousel_unstable)({
        announcement: props.announcement,
        defaultValue: props.defaultValue,
        value: props.value,
        onValueChange: props.onValueChange,
        onFinish: handleFinish
    });
    return {
        components: {
            root: 'div'
        },
        root: _reactutilities.slot.always((0, _reactutilities.getIntrinsicElementProps)('div', {
            ref: (0, _reactutilities.useMergedRefs)(ref, carouselRef),
            ...props
        }), {
            elementType: 'div'
        }),
        ...carousel
    };
};
const useTeachingPopoverCarousel_unstable = (props, ref)=>{
    const baseState = useTeachingPopoverCarouselBase_unstable(props, ref);
    const appearance = (0, _reactpopover.usePopoverContext_unstable)((context)=>context.appearance);
    return {
        ...baseState,
        appearance
    };
};
