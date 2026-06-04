'use client';
import * as React from 'react';
/**
 * @internal
 */ export const usePrevious = (value)=>{
    const ref = React.useRef(null);
    React.useEffect(()=>{
        ref.current = value;
    }, [
        value
    ]);
    // eslint-disable-next-line react-hooks/refs
    return ref.current;
};
