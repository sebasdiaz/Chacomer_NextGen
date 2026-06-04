'use client';
import { getIntrinsicElementProps, slot } from '@fluentui/react-utilities';
import { useARIAButtonProps } from '@fluentui/react-aria';
import { useNavContext_unstable } from '../NavContext';
/**
 * Create the state required to render AppItem.
 *
 * The returned state can be modified with hooks such as useAppItemStyles_unstable,
 * before being passed to renderAppItem_unstable.
 *
 * @param props - props from this instance of AppItem
 * @param ref - reference to root HTMLDivElement of AppItem
 */ export const useAppItem_unstable = (props, ref)=>{
    const { density = 'medium' } = useNavContext_unstable();
    const state = useAppItemBase_unstable(props, ref);
    return {
        ...state,
        density
    };
};
/**
 * Create the base state required to render AppItem.
 *
 * The returned state can be modified with hooks such as useAppItemStyles_unstable,
 * before being passed to renderAppItem_unstable.
 *
 * @param props - props from this instance of AppItem
 * @param ref - reference to root HTMLDivElement of AppItem
 * @returns The base state of AppItem
 */ export const useAppItemBase_unstable = (props, ref)=>{
    const { icon, as, href } = props;
    const rootElementType = as || (href ? 'a' : 'button');
    const root = slot.always(getIntrinsicElementProps(rootElementType, useARIAButtonProps(rootElementType, {
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
        icon: slot.optional(icon, {
            elementType: 'span'
        })
    };
};
