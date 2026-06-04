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
    useTeachingPopoverCarouselFooterButtonBase_unstable: function() {
        return useTeachingPopoverCarouselFooterButtonBase_unstable;
    },
    useTeachingPopoverCarouselFooterButton_unstable: function() {
        return useTeachingPopoverCarouselFooterButton_unstable;
    }
});
const _interop_require_wildcard = require("@swc/helpers/_/_interop_require_wildcard");
const _react = /*#__PURE__*/ _interop_require_wildcard._(require("react"));
const _reactutilities = require("@fluentui/react-utilities");
const _reactpopover = require("@fluentui/react-popover");
const _CarouselContext = require("../TeachingPopoverCarousel/Carousel/CarouselContext");
const _reactbutton = require("@fluentui/react-button");
const _useCarouselValues = require("../TeachingPopoverCarousel/Carousel/useCarouselValues");
const useTeachingPopoverCarouselFooterButtonBase_unstable = (props, ref)=>{
    const { navType, altText } = props;
    const selectPageByDirection = (0, _CarouselContext.useCarouselContext_unstable)((c)=>c.selectPageByDirection);
    const values = (0, _useCarouselValues.useCarouselValues_unstable)((snapshot)=>snapshot);
    const activeValue = (0, _CarouselContext.useCarouselContext_unstable)((c)=>c.value);
    const handleClick = (event)=>{
        if (event.isDefaultPrevented()) {
            return;
        }
        selectPageByDirection(event, navType);
    };
    const handleButtonClick = (0, _reactutilities.useEventCallback)((0, _reactutilities.mergeCallbacks)(handleClick, props.onClick));
    const isTrailing = _react.useMemo(()=>{
        if (!activeValue) {
            return false;
        }
        if (navType === 'prev') {
            return values.indexOf(activeValue) === 0;
        }
        return values.indexOf(activeValue) === values.length - 1;
    }, [
        navType,
        activeValue,
        values
    ]);
    /* Handle altText on trailing step */ let buttonChild = props.children;
    if (isTrailing) {
        buttonChild = altText;
    }
    return {
        navType,
        altText,
        components: {
            root: 'button'
        },
        root: _reactutilities.slot.always((0, _reactutilities.getIntrinsicElementProps)('button', {
            ref,
            ...props,
            onClick: handleButtonClick,
            children: buttonChild
        }), {
            elementType: 'button'
        })
    };
};
const useTeachingPopoverCarouselFooterButton_unstable = (props, ref)=>{
    const baseState = useTeachingPopoverCarouselFooterButtonBase_unstable(props, ref);
    const popoverAppearance = (0, _reactpopover.usePopoverContext_unstable)((context)=>context.appearance);
    let buttonAppearanceType;
    if (props.navType === 'next') {
        buttonAppearanceType = popoverAppearance === 'brand' ? undefined : 'primary';
    } else {
        buttonAppearanceType = popoverAppearance === 'brand' ? 'outline' : undefined;
    }
    const buttonState = (0, _reactbutton.useButton_unstable)({
        appearance: buttonAppearanceType,
        ...props
    }, ref);
    return {
        ...buttonState,
        navType: baseState.navType,
        altText: baseState.altText,
        root: baseState.root,
        popoverAppearance
    };
};
