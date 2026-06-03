import * as React from 'react';
import {
    DataGrid,
    DataGridBody,
    DataGridCell,
    DataGridHeader,
    DataGridHeaderCell,
    DataGridRow,
    TableColumnDefinition,
    TableColumnSizingOptions,
    createTableColumn,
    Spinner,
    MessageBar,
    MessageBarBody,
    tokens,
    makeStyles,
} from '@fluentui/react-components';

export interface IDeviceRegistration {
    msauto_deviceregistrationid: string;
    companyName: string;
    deviceNumber: string;
    deviceName: string;
    deviceDescription: string;
    brandName: string;
    className: string;
    modelName: string;
    modelCodeName: string;
    configCodeName: string;
    exteriorName: string;
    interiorName: string;
}

export interface IDeviceRegistrationGridProps {
    items: IDeviceRegistration[];
    isLoading: boolean;
    errorMessage: string | null;
}

const useStyles = makeStyles({
    spinnerWrapper: {
        display: 'flex',
        justifyContent: 'center',
        padding: '16px',
    },
    emptyMessage: {
        color: tokens.colorNeutralForeground3,
        fontStyle: 'italic',
        padding: '8px 0',
    },
    container: {
        padding: '8px',
        minHeight: '60px',
    },
});

const columnSizingOptions: TableColumnSizingOptions = {
    companyName:       { idealWidth: 140, minWidth: 100 },
    deviceNumber:      { idealWidth: 120, minWidth: 80  },
    deviceName:        { idealWidth: 160, minWidth: 100 },
    deviceDescription: { idealWidth: 200, minWidth: 120 },
    brandName:         { idealWidth: 120, minWidth: 80  },
    className:         { idealWidth: 120, minWidth: 80  },
    modelName:         { idealWidth: 140, minWidth: 80  },
    modelCodeName:     { idealWidth: 120, minWidth: 80  },
    configCodeName:    { idealWidth: 140, minWidth: 80  },
    exteriorName:      { idealWidth: 120, minWidth: 80  },
    interiorName:      { idealWidth: 120, minWidth: 80  },
};

const columns: TableColumnDefinition<IDeviceRegistration>[] = [
    createTableColumn<IDeviceRegistration>({
        columnId: 'companyName',
        renderHeaderCell: () => 'Company',
        renderCell: (item) => item.companyName || '—',
    }),
    createTableColumn<IDeviceRegistration>({
        columnId: 'deviceNumber',
        renderHeaderCell: () => 'Device Number',
        renderCell: (item) => item.deviceNumber || '—',
    }),
    createTableColumn<IDeviceRegistration>({
        columnId: 'deviceName',
        renderHeaderCell: () => 'Device Name',
        renderCell: (item) => item.deviceName || '—',
    }),
    createTableColumn<IDeviceRegistration>({
        columnId: 'deviceDescription',
        renderHeaderCell: () => 'Description',
        renderCell: (item) => item.deviceDescription || '—',
    }),
    createTableColumn<IDeviceRegistration>({
        columnId: 'brandName',
        renderHeaderCell: () => 'Brand',
        renderCell: (item) => item.brandName || '—',
    }),
    createTableColumn<IDeviceRegistration>({
        columnId: 'className',
        renderHeaderCell: () => 'Class',
        renderCell: (item) => item.className || '—',
    }),
    createTableColumn<IDeviceRegistration>({
        columnId: 'modelName',
        renderHeaderCell: () => 'Model',
        renderCell: (item) => item.modelName || '—',
    }),
    createTableColumn<IDeviceRegistration>({
        columnId: 'modelCodeName',
        renderHeaderCell: () => 'Model Code',
        renderCell: (item) => item.modelCodeName || '—',
    }),
    createTableColumn<IDeviceRegistration>({
        columnId: 'configCodeName',
        renderHeaderCell: () => 'Config Code',
        renderCell: (item) => item.configCodeName || '—',
    }),
    createTableColumn<IDeviceRegistration>({
        columnId: 'exteriorName',
        renderHeaderCell: () => 'Exterior',
        renderCell: (item) => item.exteriorName || '—',
    }),
    createTableColumn<IDeviceRegistration>({
        columnId: 'interiorName',
        renderHeaderCell: () => 'Interior',
        renderCell: (item) => item.interiorName || '—',
    }),
];

export const DeviceRegistrationGridView: React.FC<IDeviceRegistrationGridProps> = ({ items, isLoading, errorMessage }) => {
    const styles = useStyles();

    if (isLoading) {
        return (
            <div className={styles.spinnerWrapper}>
                <Spinner label="Loading device registrations..." size="small" />
            </div>
        );
    }

    if (errorMessage) {
        return (
            <MessageBar intent="error">
                <MessageBarBody>{errorMessage}</MessageBarBody>
            </MessageBar>
        );
    }

    if (items.length === 0) {
        return (
            <div className={styles.container}>
                <span className={styles.emptyMessage}>No device registrations found.</span>
            </div>
        );
    }

    return (
        <div className={styles.container}>
            <DataGrid
                items={items}
                columns={columns}
                getRowId={(item: IDeviceRegistration) => item.msauto_deviceregistrationid}
                focusMode="composite"
                resizableColumns
                columnSizingOptions={columnSizingOptions}
            >
                <DataGridHeader>
                    <DataGridRow>
                        {({ renderHeaderCell }) => (
                            <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
                        )}
                    </DataGridRow>
                </DataGridHeader>
                <DataGridBody<IDeviceRegistration>>
                    {({ item, rowId }) => (
                        <DataGridRow<IDeviceRegistration> key={rowId}>
                            {({ renderCell }) => (
                                <DataGridCell>{renderCell(item)}</DataGridCell>
                            )}
                        </DataGridRow>
                    )}
                </DataGridBody>
            </DataGrid>
        </div>
    );
};
