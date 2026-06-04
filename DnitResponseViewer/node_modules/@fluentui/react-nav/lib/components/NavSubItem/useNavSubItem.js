'use client';
import * as React from 'react';
import { getIntrinsicElementProps, slot, useEventCallback, isHTMLElement } from '@fluentui/react-utilities';
import { useARIAButtonProps } from '@fluentui/react-aria';
import { useNavContext_unstable } from '../NavContext';
import { useNavCategoryContext_unstable } from '../NavCategoryContext';
/**
 * Create the state required to render NavSubItem.
 *
 * The returned state can be modified with hooks such as useNavSubItemStyles_unstable,
 * before being passed to renderNavSubItem_unstable.
 *
 * @param props - props from this instance of NavSubItem
 * @param ref - reference to root HTMLButtonElement of NavSubItem
 */ export const useNavSubItem_unstable = (props, ref)=>{
    const { density = 'medium' } = useNavContext_unstable();
    const state = useNavSubItemBase_unstable(props, ref);
    return {
        ...state,
        density
    };
};
/**
 * Base state used in rendering NavSubItem, excluding any design-related properties such as `density`.
 *
 * @param props - props from this instance of NavSubItem
 * @param ref - reference to root HTMLButtonElement of NavSubItem
 * @returns - The base state of NavSubItem
 */ export const useNavSubItemBase_unstable = (props, ref)=>{
    const { onClick, value: subItemValue, as, href } = props;
    const { selectedValue, onRegister, onUnregister, onSelect } = useNavContext_unstable();
    const { value: parentCategoryValue } = useNavCategoryContext_unstable();
    const rootElementType = as || (href ? 'a' : 'button');
    const selected = selectedValue === subItemValue;
    const innerRef = React.useRef(null);
    const onNavSubItemClick = useEventCallback((event)=>{
        onClick === null || onClick === void 0 ? void 0 : onClick(event);
        if (!event.defaultPrevented && isHTMLElement(event.target)) {
            onSelect(event, {
                type: 'click',
                event,
                value: subItemValue,
                categoryValue: parentCategoryValue
            });
        }
    });
    const root = slot.always(getIntrinsicElementProps(rootElementType, useARIAButtonProps(rootElementType, {
        'aria-current': selected ? 'page' : 'false',
        ...props
    })), {
        elementType: rootElementType,
        defaultProps: {
            ref: ref,
            type: rootElementType
        }
    });
    root.onClick = onNavSubItemClick;
    React.useEffect(()=>{
        onRegister({
            value: subItemValue,
            ref: innerRef
        });
        return ()=>{
            onUnregister({
                value: subItemValue,
                ref: innerRef
            });
        };
    }, [
        onRegister,
        onUnregister,
        innerRef,
        subItemValue
    ]);
    return {
        components: {
            root: rootElementType
        },
        root,
        selected,
        value: subItemValue
    };
};
