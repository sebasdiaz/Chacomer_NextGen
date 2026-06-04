'use client';
import * as React from 'react';
import { getIntrinsicElementProps, useEventCallback, slot } from '@fluentui/react-utilities';
import { Dismiss12Regular, Lightbulb16Regular } from '@fluentui/react-icons';
import { usePopoverContext_unstable } from '@fluentui/react-popover';
/**
 * Base hook that builds TeachingPopoverHeader state for behavior and structure only.
 * Does not add styling defaults such as icon JSX or `appearance` from popover context.
 * @param props - TeachingPopoverHeader properties
 * @param ref - reference to root HTMLElement of TeachingPopoverHeader
 */ export const useTeachingPopoverHeaderBase_unstable = (props, ref)=>{
    const { dismissButton, icon } = props;
    const setOpen = usePopoverContext_unstable((context)=>context.setOpen);
    const triggerRef = usePopoverContext_unstable((context)=>context.triggerRef);
    const onDismissButtonClick = useEventCallback((ev)=>{
        if (!ev.defaultPrevented) {
            setOpen(ev, false);
        }
        if (triggerRef.current) {
            triggerRef.current.focus();
        }
    });
    return {
        components: {
            root: 'div',
            dismissButton: 'button',
            icon: 'div'
        },
        root: slot.always(getIntrinsicElementProps('div', {
            ref,
            ...props
        }), {
            elementType: 'div'
        }),
        icon: slot.optional(icon, {
            renderByDefault: true,
            defaultProps: {
                'aria-hidden': true
            },
            elementType: 'div'
        }),
        dismissButton: slot.optional(dismissButton, {
            renderByDefault: true,
            defaultProps: {
                role: 'button',
                'aria-label': 'dismiss',
                onClick: onDismissButtonClick
            },
            elementType: 'button'
        })
    };
};
/**
 * Returns the props and state required to render the component
 * @param props - TeachingPopoverHeader properties
 * @param ref - reference to root HTMLElement of TeachingPopoverHeader
 */ export const useTeachingPopoverHeader_unstable = (props, ref)=>{
    const baseState = useTeachingPopoverHeaderBase_unstable(props, ref);
    const appearance = usePopoverContext_unstable((context)=>context.appearance);
    const icon = baseState.icon && baseState.icon.children === undefined ? {
        ...baseState.icon,
        children: /*#__PURE__*/ React.createElement(Lightbulb16Regular, null)
    } : baseState.icon;
    const dismissButton = baseState.dismissButton && baseState.dismissButton.children === undefined ? {
        ...baseState.dismissButton,
        children: /*#__PURE__*/ React.createElement(Dismiss12Regular, null)
    } : baseState.dismissButton;
    return {
        ...baseState,
        appearance,
        icon,
        dismissButton
    };
};
