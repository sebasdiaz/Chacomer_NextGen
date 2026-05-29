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

export interface IChildContact {
    contactid: string;
    fullname: string;
    legalEntityName: string;
    customerGroupName: string;
}

export interface IChildContactsGridProps {
    contacts: IChildContact[];
    isLoading: boolean;
    errorMessage: string | null;
}

const useStyles = makeStyles({
    container: {
        padding: '8px',
        minHeight: '60px',
    },
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
});

const columns: TableColumnDefinition<IChildContact>[] = [
    createTableColumn<IChildContact>({
        columnId: 'fullname',
        renderHeaderCell: () => 'Contact Name',
        renderCell: (item) => item.fullname || '—',
    }),
    createTableColumn<IChildContact>({
        columnId: 'legalEntity',
        renderHeaderCell: () => 'Legal Entity',
        renderCell: (item) => item.legalEntityName || '—',
    }),
    createTableColumn<IChildContact>({
        columnId: 'customerGroup',
        renderHeaderCell: () => 'Customer Group',
        renderCell: (item) => item.customerGroupName || '—',
    }),
];

export const ChildContactsGrid: React.FC<IChildContactsGridProps> = ({ contacts, isLoading, errorMessage }) => {
    const styles = useStyles();

    if (isLoading) {
        return (
            <div className={styles.spinnerWrapper}>
                <Spinner label="Loading child contacts..." size="small" />
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

    if (contacts.length === 0) {
        return (
            <div className={styles.container}>
                <span className={styles.emptyMessage}>No child contacts found.</span>
            </div>
        );
    }

    return (
        <div className={styles.container}>
            <DataGrid
                items={contacts}
                columns={columns}
                getRowId={(item: IChildContact) => item.contactid}
                focusMode="composite"
            >
                <DataGridHeader>
                    <DataGridRow>
                        {({ renderHeaderCell }) => (
                            <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
                        )}
                    </DataGridRow>
                </DataGridHeader>
                <DataGridBody<IChildContact>>
                    {({ item, rowId }) => (
                        <DataGridRow<IChildContact> key={rowId}>
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
