'use client';
import * as React from 'react';
import { useFluent_unstable as useFluent } from '@fluentui/react-shared-contexts';
import { useOverflowContext } from '../../overflowContext';
/**
 * Renderless opt-in component that observes the `<Overflow>` container for
 * direct-child DOM mutations and triggers an overflow recompute. Drop it
 * inside `<Overflow>` when items can be reordered, added, or removed via
 * React state without a container resize — Fluent's overflow manager only
 * listens to `ResizeObserver` otherwise, so reorders leave visibility flags
 * stale.
 *
 * @example
 * ```tsx
 * <Overflow>
 *   <div>
 *     <OverflowReorderObserver />
 *     {items.map(id => <OverflowItem key={id} id={id} priority={1}>{id}</OverflowItem>)}
 *   </div>
 * </Overflow>
 * ```
 */ export const OverflowReorderObserver = ()=>{
    const containerRef = useOverflowContext((v)=>v.containerRef);
    const updateOverflow = useOverflowContext((v)=>v.updateOverflow);
    const { targetDocument } = useFluent();
    const targetWindow = targetDocument === null || targetDocument === void 0 ? void 0 : targetDocument.defaultView;
    React.useEffect(()=>{
        const el = containerRef === null || containerRef === void 0 ? void 0 : containerRef.current;
        if (!el || !(targetWindow === null || targetWindow === void 0 ? void 0 : targetWindow.MutationObserver)) {
            return;
        }
        const mutationObserver = new targetWindow.MutationObserver(()=>updateOverflow());
        mutationObserver.observe(el, {
            childList: true
        });
        return ()=>mutationObserver.disconnect();
    }, [
        containerRef,
        updateOverflow,
        targetWindow
    ]);
    return null;
};
