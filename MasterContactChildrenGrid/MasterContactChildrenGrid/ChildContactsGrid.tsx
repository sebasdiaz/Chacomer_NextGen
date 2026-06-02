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

const SELECT_FIELDS = "contactid,fullname,_msdyn_company_value,_msdyn_customergroupid_value";
const EXPAND_FIELDS = "msdyn_company($select=cdm_name),msdyn_customergroupid($select=msdyn_description)";

interface IContactEntity {
    contactid: string;
    fullname: string;
    msdyn_company?: { cdm_name?: string };
    msdyn_customergroupid?: { msdyn_description?: string };
}

interface IChildContact {
    contactid: string;
    fullname: string;
    legalEntityName: string;
    customerGroupName: string;
}

export interface IChildContactsGridProps {
    masterContactId: string | null;
    webAPI: ComponentFramework.WebApi;
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

const columnSizingOptions: TableColumnSizingOptions = {
    fullname: { idealWidth: 200, minWidth: 140 },
    legalEntity: { idealWidth: 180, minWidth: 120 },
    customerGroup: { idealWidth: 160, minWidth: 100 },
};

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

export const ChildContactsGrid: React.FC<IChildContactsGridProps> = ({ masterContactId, webAPI }) => {
    const styles = useStyles();
    const [contacts, setContacts] = React.useState<IChildContact[]>([]);
    const [isLoading, setIsLoading] = React.useState(false);
    const [errorMessage, setErrorMessage] = React.useState<string | null>(null);

    React.useEffect(() => {
        if (!masterContactId) {
            setContacts([]);
            return;
        }

        let cancelled = false;
        setIsLoading(true);
        setErrorMessage(null);

        const options = `?$select=${SELECT_FIELDS}&$expand=${EXPAND_FIELDS}&$filter=_axx_mastercontactid_value eq '${masterContactId}'`;

        webAPI.retrieveMultipleRecords("contact", options)
            .then((result) => {
                if (cancelled) return undefined;
                const mapped = (result.entities as unknown as IContactEntity[]).map((e) => ({
                    contactid: e.contactid,
                    fullname: e.fullname ?? "",
                    legalEntityName: e.msdyn_company?.cdm_name ?? "",
                    customerGroupName: e.msdyn_customergroupid?.msdyn_description ?? "",
                }));
                setContacts(mapped);
                setIsLoading(false);
                return undefined;
            })
            .catch((error: Error) => {
                if (cancelled) return;
                setErrorMessage(`Error loading contacts: ${error.message}`);
                setIsLoading(false);
            });

        return () => { cancelled = true; };
    }, [masterContactId]);

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
