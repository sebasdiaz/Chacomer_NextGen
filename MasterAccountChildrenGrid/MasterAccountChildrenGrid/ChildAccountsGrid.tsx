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

const SELECT_FIELDS = "accountid,name,_msdyn_company_value,_msdyn_customergroupid_value,_msdyn_paymentterm_value";
const EXPAND_FIELDS = "msdyn_company($select=cdm_name,cdm_companycode),msdyn_customergroupid($select=msdyn_description),msdyn_paymentterm($select=msdyn_description)";

interface IAccountEntity {
    accountid: string;
    name: string;
    msdyn_company?: { cdm_name?: string; cdm_companycode?: string };
    msdyn_customergroupid?: { msdyn_description?: string };
    msdyn_paymentterm?: { msdyn_description?: string };
}

interface IChildAccount {
    accountid: string;
    name: string;
    legalEntityName: string;
    legalEntityCode: string;
    customerGroupName: string;
    paymentTermsName: string;
}

export interface IChildAccountsGridProps {
    masterAccountId: string | null;
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
    name: { idealWidth: 200, minWidth: 140 },
    legalEntity: { idealWidth: 180, minWidth: 120 },
    legalEntityCode: { idealWidth: 120, minWidth: 80 },
    customerGroup: { idealWidth: 160, minWidth: 100 },
    paymentTerms: { idealWidth: 140, minWidth: 100 },
};

const columns: TableColumnDefinition<IChildAccount>[] = [
    createTableColumn<IChildAccount>({
        columnId: 'name',
        renderHeaderCell: () => 'Account Name',
        renderCell: (item) => item.name || '—',
    }),
    createTableColumn<IChildAccount>({
        columnId: 'legalEntity',
        renderHeaderCell: () => 'Legal Entity',
        renderCell: (item) => item.legalEntityName || '—',
    }),
    createTableColumn<IChildAccount>({
        columnId: 'legalEntityCode',
        renderHeaderCell: () => 'Company Code',
        renderCell: (item) => item.legalEntityCode || '—',
    }),
    createTableColumn<IChildAccount>({
        columnId: 'customerGroup',
        renderHeaderCell: () => 'Customer Group',
        renderCell: (item) => item.customerGroupName || '—',
    }),
    createTableColumn<IChildAccount>({
        columnId: 'paymentTerms',
        renderHeaderCell: () => 'Payment Terms',
        renderCell: (item) => item.paymentTermsName || '—',
    }),
];

export const ChildAccountsGrid: React.FC<IChildAccountsGridProps> = ({ masterAccountId, webAPI }) => {
    const styles = useStyles();
    const [accounts, setAccounts] = React.useState<IChildAccount[]>([]);
    const [isLoading, setIsLoading] = React.useState(false);
    const [errorMessage, setErrorMessage] = React.useState<string | null>(null);

    React.useEffect(() => {
        if (!masterAccountId) {
            setAccounts([]);
            return;
        }

        let cancelled = false;
        setIsLoading(true);
        setErrorMessage(null);

        const options = `?$select=${SELECT_FIELDS}&$expand=${EXPAND_FIELDS}&$filter=_axx_masteraccountid_value eq '${masterAccountId}'`;

        webAPI.retrieveMultipleRecords("account", options)
            .then((result) => {
                if (cancelled) return undefined;
                const mapped = (result.entities as unknown as IAccountEntity[]).map((e) => ({
                    accountid: e.accountid,
                    name: e.name ?? "",
                    legalEntityName: e.msdyn_company?.cdm_name ?? "",
                    legalEntityCode: e.msdyn_company?.cdm_companycode ?? "",
                    customerGroupName: e.msdyn_customergroupid?.msdyn_description ?? "",
                    paymentTermsName: e.msdyn_paymentterm?.msdyn_description ?? "",
                }));
                setAccounts(mapped);
                setIsLoading(false);
                return undefined;
            })
            .catch((error: Error) => {
                if (cancelled) return;
                setErrorMessage(`Error loading accounts: ${error.message}`);
                setIsLoading(false);
            });

        return () => { cancelled = true; };
    }, [masterAccountId]);

    if (isLoading) {
        return (
            <div className={styles.spinnerWrapper}>
                <Spinner label="Loading child accounts..." size="small" />
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

    if (accounts.length === 0) {
        return (
            <div className={styles.container}>
                <span className={styles.emptyMessage}>No child accounts found.</span>
            </div>
        );
    }

    return (
        <div className={styles.container}>
            <DataGrid
                items={accounts}
                columns={columns}
                getRowId={(item: IChildAccount) => item.accountid}
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
                <DataGridBody<IChildAccount>>
                    {({ item, rowId }) => (
                        <DataGridRow<IChildAccount> key={rowId}>
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
