'use client';
import * as React from 'react';
import { getIntrinsicElementProps, useControllableState, useEventCallback, mergeCallbacks, useMergedRefs, slot } from '@fluentui/react-utilities';
import { useArrowNavigationGroup, useFocusFinders } from '@fluentui/react-tabster';
import { useFluent_unstable as useFluent } from '@fluentui/react-shared-contexts';
import { interactionTagSecondaryClassNames } from '../InteractionTagSecondary/useInteractionTagSecondaryStyles.styles';
/**
 * Create the base state required to render TagGroup, without design-only props.
 *
 * @param props - props from this instance of TagGroup (without appearance, size)
 * @param ref - reference to root HTMLDivElement of TagGroup
 */ export const useTagGroupBase_unstable = (props, ref)=>{
    const { onDismiss, disabled = false, defaultSelectedValues, dismissible = false, role = 'toolbar', onTagSelect, selectedValues, ...rest } = props;
    const [items, setItems] = useControllableState({
        defaultState: defaultSelectedValues,
        state: selectedValues,
        initialState: []
    });
    const handleTagDismiss = useEventCallback((e, data)=>{
        onDismiss === null || onDismiss === void 0 ? void 0 : onDismiss(e, data);
    });
    const handleTagSelect = useEventCallback(mergeCallbacks(onTagSelect, (_, data)=>{
        if (items.includes(data.value)) {
            setItems(items.filter((item)=>item !== data.value));
        } else {
            setItems([
                ...items,
                data.value
            ]);
        }
    }));
    return {
        handleTagDismiss,
        handleTagSelect: onTagSelect ? handleTagSelect : undefined,
        selectedValues: items,
        role,
        disabled,
        dismissible,
        components: {
            root: 'div'
        },
        root: slot.always(getIntrinsicElementProps('div', {
            ref,
            role,
            'aria-disabled': disabled,
            ...rest
        }), {
            elementType: 'div'
        })
    };
};
/**
 * Create the state required to render TagGroup.
 *
 * The returned state can be modified with hooks such as useTagGroupStyles_unstable,
 * before being passed to renderTagGroup_unstable.
 *
 * @param props - props from this instance of TagGroup
 * @param ref - reference to root HTMLDivElement of TagGroup
 */ export const useTagGroup_unstable = (props, ref)=>{
    const { size = 'medium', appearance = 'filled' } = props;
    const { targetDocument } = useFluent();
    const { findNextFocusable, findPrevFocusable } = useFocusFinders();
    const arrowNavigationProps = useArrowNavigationGroup({
        circular: true,
        axis: 'both',
        memorizeCurrent: true
    });
    const innerRef = React.useRef(null);
    const mergedRef = useMergedRefs(ref, innerRef);
    const enhancedOnDismiss = useEventCallback((e, data)=>{
        var _props_onDismiss;
        (_props_onDismiss = props.onDismiss) === null || _props_onDismiss === void 0 ? void 0 : _props_onDismiss.call(props, e, data);
        const container = innerRef.current;
        const activeElement = targetDocument === null || targetDocument === void 0 ? void 0 : targetDocument.activeElement;
        if (container === null || container === void 0 ? void 0 : container.contains(activeElement)) {
            // focus on next tag only if the active element is within the current tag group
            const next = findNextFocusable(activeElement, {
                container
            });
            if (next) {
                next.focus();
                return;
            }
            // if there is no next focusable, focus on the previous focusable
            if (activeElement === null || activeElement === void 0 ? void 0 : activeElement.className.includes(interactionTagSecondaryClassNames.root)) {
                const prev = findPrevFocusable(activeElement.parentElement, {
                    container
                });
                prev === null || prev === void 0 ? void 0 : prev.focus();
            } else {
                const prev = findPrevFocusable(activeElement, {
                    container
                });
                prev === null || prev === void 0 ? void 0 : prev.focus();
            }
        }
    });
    return {
        ...useTagGroupBase_unstable({
            ...arrowNavigationProps,
            ...props,
            onDismiss: enhancedOnDismiss
        }, mergedRef),
        size,
        appearance
    };
};
