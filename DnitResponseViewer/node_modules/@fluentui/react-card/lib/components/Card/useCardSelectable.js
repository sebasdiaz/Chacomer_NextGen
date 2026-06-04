'use client';
import * as React from 'react';
import { mergeCallbacks, slot, useControllableState } from '@fluentui/react-utilities';
import { Enter } from '@fluentui/keyboard-keys';
/**
 * Create the state related to selectable cards.
 *
 * This internal hook controls all the logic for selectable cards and is
 * intended to be used alongside with useCardBase_unstable / useCard_unstable.
 *
 * @internal
 * @param props - props from this instance of Card
 * @param a11yProps - accessibility props shared between elements of the card
 * @param options - optional behavior overrides such as a focus-aware restriction predicate
 */ export const useCardSelectable = (props, { referenceLabel, referenceId })=>{
    const { checkbox = {}, onSelectionChange, floatingAction, onClick, onKeyDown, disabled, shouldRestrictTriggerAction } = props;
    const checkboxRef = React.useRef(null);
    const [selected, setSelected] = useControllableState({
        state: props.selected,
        defaultState: props.defaultSelected,
        initialState: false
    });
    const selectable = [
        props.selected,
        props.defaultSelected,
        onSelectionChange
    ].some((prop)=>typeof prop !== 'undefined');
    const [selectFocused, setSelectFocused] = React.useState(false);
    const onChangeHandler = React.useCallback((event)=>{
        if (disabled) {
            return;
        }
        const isCheckboxOrFloatingActionTarget = checkboxRef.current === event.target;
        if (!isCheckboxOrFloatingActionTarget && (shouldRestrictTriggerAction === null || shouldRestrictTriggerAction === void 0 ? void 0 : shouldRestrictTriggerAction(event))) {
            return;
        }
        const newCheckedValue = !selected;
        setSelected(newCheckedValue);
        if (onSelectionChange) {
            onSelectionChange(event, {
                selected: newCheckedValue
            });
        }
    }, [
        disabled,
        onSelectionChange,
        selected,
        setSelected,
        shouldRestrictTriggerAction
    ]);
    const onKeyDownHandler = React.useCallback((event)=>{
        if ([
            Enter
        ].includes(event.key)) {
            event.preventDefault();
            onChangeHandler(event);
        }
    }, [
        onChangeHandler
    ]);
    const checkboxSlot = React.useMemo(()=>{
        if (!selectable || floatingAction) {
            return;
        }
        const selectableCheckboxProps = {};
        if (referenceId) {
            selectableCheckboxProps['aria-labelledby'] = referenceId;
        } else if (referenceLabel) {
            selectableCheckboxProps['aria-label'] = referenceLabel;
        }
        // eslint-disable-next-line react-hooks/refs
        return slot.optional(checkbox, {
            defaultProps: {
                ref: checkboxRef,
                type: 'checkbox',
                checked: selected,
                disabled,
                onChange: (event)=>onChangeHandler(event),
                onFocus: ()=>setSelectFocused(true),
                onBlur: ()=>setSelectFocused(false),
                ...selectableCheckboxProps
            },
            elementType: 'input'
        });
    }, [
        checkbox,
        disabled,
        floatingAction,
        selected,
        selectable,
        onChangeHandler,
        referenceId,
        referenceLabel
    ]);
    const floatingActionSlot = React.useMemo(()=>{
        if (!floatingAction) {
            return;
        }
        // eslint-disable-next-line react-hooks/refs
        return slot.optional(floatingAction, {
            defaultProps: {
                ref: checkboxRef
            },
            elementType: 'div'
        });
    }, [
        floatingAction
    ]);
    const selectableCardProps = React.useMemo(()=>{
        if (!selectable) {
            return null;
        }
        return {
            // eslint-disable-next-line react-hooks/refs
            onClick: mergeCallbacks(onClick, onChangeHandler),
            // eslint-disable-next-line react-hooks/refs
            onKeyDown: mergeCallbacks(onKeyDown, onKeyDownHandler)
        };
    }, [
        selectable,
        onChangeHandler,
        onClick,
        onKeyDown,
        onKeyDownHandler
    ]);
    return {
        selected,
        selectable,
        selectFocused,
        selectableCardProps,
        checkboxSlot,
        floatingActionSlot
    };
};
