'use client';
import { getIntrinsicElementProps, slot, useEventCallback, useMergedRefs } from '@fluentui/react-utilities';
import { usePopoverContext_unstable } from '@fluentui/react-popover';
import { useCarousel_unstable } from './Carousel/Carousel';
/**
 * Base hook that builds TeachingPopoverCarousel state for behavior and structure only.
 * Does not read `appearance` from the popover context.
 * @param props - TeachingPopoverCarousel properties
 * @param ref - reference to root HTMLElement of TeachingPopoverCarousel
 */ export const useTeachingPopoverCarouselBase_unstable = (props, ref)=>{
    const toggleOpen = usePopoverContext_unstable((c)=>c.toggleOpen);
    const handleFinish = useEventCallback((event, data)=>{
        var _props_onFinish;
        (_props_onFinish = props.onFinish) === null || _props_onFinish === void 0 ? void 0 : _props_onFinish.call(props, event, data);
        toggleOpen(event);
    });
    const { carousel, carouselRef } = useCarousel_unstable({
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
        root: slot.always(getIntrinsicElementProps('div', {
            ref: useMergedRefs(ref, carouselRef),
            ...props
        }), {
            elementType: 'div'
        }),
        ...carousel
    };
};
export const useTeachingPopoverCarousel_unstable = (props, ref)=>{
    const baseState = useTeachingPopoverCarouselBase_unstable(props, ref);
    const appearance = usePopoverContext_unstable((context)=>context.appearance);
    return {
        ...baseState,
        appearance
    };
};
