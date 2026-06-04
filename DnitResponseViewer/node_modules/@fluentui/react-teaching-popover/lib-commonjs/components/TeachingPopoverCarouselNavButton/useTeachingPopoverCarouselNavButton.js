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
    useTeachingPopoverCarouselNavButtonBase_unstable: function() {
        return useTeachingPopoverCarouselNavButtonBase_unstable;
    },
    useTeachingPopoverCarouselNavButton_unstable: function() {
        return useTeachingPopoverCarouselNavButton_unstable;
    }
});
const _reactaria = require("@fluentui/react-aria");
const _reactpopover = require("@fluentui/react-popover");
const _reacttabster = require("@fluentui/react-tabster");
const _reactutilities = require("@fluentui/react-utilities");
const _CarouselContext = require("../TeachingPopoverCarousel/Carousel/CarouselContext");
const _valueIdContext = require("../TeachingPopoverCarouselNav/valueIdContext");
const useTeachingPopoverCarouselNavButtonBase_unstable = (props, ref)=>{
    const { onClick, as = 'a' } = props;
    const value = (0, _valueIdContext.useValueIdContext)();
    const selectPageByValue = (0, _CarouselContext.useCarouselContext_unstable)((c)=>c.selectPageByValue);
    const isSelected = (0, _CarouselContext.useCarouselContext_unstable)((c)=>c.value === value);
    const handleClick = (0, _reactutilities.useEventCallback)((event)=>{
        onClick === null || onClick === void 0 ? void 0 : onClick(event);
        if (!event.defaultPrevented && (0, _reactutilities.isHTMLElement)(event.target)) {
            selectPageByValue(event, value);
        }
    });
    const _carouselButton = _reactutilities.slot.always((0, _reactutilities.getIntrinsicElementProps)(as, (0, _reactaria.useARIAButtonProps)(props.as, props)), {
        elementType: 'button',
        defaultProps: {
            ref: ref,
            role: 'tab',
            type: 'button',
            'aria-selected': `${!!isSelected}`
        }
    });
    _carouselButton.onClick = handleClick;
    return {
        isSelected,
        components: {
            root: 'button'
        },
        root: _carouselButton
    };
};
const useTeachingPopoverCarouselNavButton_unstable = (props, ref)=>{
    const baseState = useTeachingPopoverCarouselNavButtonBase_unstable(props, ref);
    const appearance = (0, _reactpopover.usePopoverContext_unstable)((context)=>context.appearance);
    const defaultTabProps = (0, _reacttabster.useTabsterAttributes)({
        focusable: {
            isDefault: baseState.isSelected
        }
    });
    return {
        ...baseState,
        // tabster attrs act as defaults: base's root keys win over them, matching the
        // previous `defaultProps` merging behavior in slot.always.
        root: {
            ...defaultTabProps,
            ...baseState.root
        },
        appearance
    };
};
