import * as React from 'react';
import {
    DataGrid,
    DataGridBody,
    DataGridCell,
    DataGridHeader,
    DataGridHeaderCell,
    DataGridRow,
    TableColumnDefinition,
    createTableColumn,
    Spinner,
    MessageBar,
    MessageBarBody,
    tokens,
    makeStyles,
} from '@fluentui/react-components';

export interface IDeviceRegistration {
    msauto_deviceregistrationid: string;
    msauto_name: string;
    msauto_deviceid: string;
    companyName: string;
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

const columns: TableColumnDefinition<IDeviceRegistration>[] = [
    createTableColumn<IDeviceRegistration>({
        columnId: 'msauto_name',
        renderHeaderCell: () => 'Name',
        renderCell: (item) => item.msauto_name || '—',
    }),
    createTableColumn<IDeviceRegistration>({
        columnId: 'msauto_deviceid',
        renderHeaderCell: () => 'Device ID',
        renderCell: (item) => item.msauto_deviceid || '—',
    }),
    createTableColumn<IDeviceRegistration>({
        columnId: 'a365_company',
        renderHeaderCell: () => 'Company',
        renderCell: (item) => item.companyName || '—',
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
