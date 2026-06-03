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

// ── OData query constants ────────────────────────────────────────────────────

const CHILD_CONTACT_SELECT = "?$select=contactid&$filter=_axx_mastercontactid_value eq '";

const DEVICE_ENTITY = "msauto_deviceregistration";

const DEVICE_SELECT = "msauto_deviceregistrationid,_a365_company_value,_a365_contactid_value";

const DEVICE_EXPAND = [
    "a365_company($select=cdm_name),",
    "msauto_DeviceId(",
        "$select=msauto_devicenumber,msauto_name,msauto_description;",
        "$expand=",
            "msauto_DeviceBrandId($select=msauto_name),",
            "msauto_DeviceClassId($select=msauto_name),",
            "msauto_DeviceModelId($select=msauto_name),",
            "msauto_DeviceModelCodeId($select=msauto_name),",
            "a365_configurationcodeid($select=msauto_name),",
            "a365_deviceexteriorid($select=a365_name),",
            "a365_deviceinteriorid($select=a365_name)",
    ")",
].join("");

// ── Interfaces ───────────────────────────────────────────────────────────────

interface IChildContact {
    contactid: string;
}

interface IDeviceEntity {
    msauto_deviceregistrationid: string;
    a365_company?: { cdm_name?: string };
    msauto_DeviceId?: {
        msauto_devicenumber?: string;
        msauto_name?: string;
        msauto_description?: string;
        msauto_DeviceBrandId?: { msauto_name?: string };
        msauto_DeviceClassId?: { msauto_name?: string };
        msauto_DeviceModelId?: { msauto_name?: string };
        msauto_DeviceModelCodeId?: { msauto_name?: string };
        a365_configurationcodeid?: { msauto_name?: string };
        a365_deviceexteriorid?: { a365_name?: string };
        a365_deviceinteriorid?: { a365_name?: string };
    };
}

interface IDeviceRegistration {
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
    masterContactId: string | null;
    webAPI: ComponentFramework.WebApi;
}

// ── Styles ───────────────────────────────────────────────────────────────────

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

// ── Columns ──────────────────────────────────────────────────────────────────

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

// ── Component ────────────────────────────────────────────────────────────────

export const DeviceRegistrationGridView: React.FC<IDeviceRegistrationGridProps> = ({ masterContactId, webAPI }) => {
    const styles = useStyles();
    const [items, setItems] = React.useState<IDeviceRegistration[]>([]);
    const [isLoading, setIsLoading] = React.useState(false);
    const [errorMessage, setErrorMessage] = React.useState<string | null>(null);

    React.useEffect(() => {
        if (!masterContactId) {
            setItems([]);
            return;
        }

        let cancelled = false;
        setIsLoading(true);
        setErrorMessage(null);

        const load = async (): Promise<void> => {
            // Step 1: resolve child contact IDs linked to the master
            const childResult = await webAPI.retrieveMultipleRecords(
                "contact",
                `${CHILD_CONTACT_SELECT}${masterContactId}'`
            );
            const childIds = (childResult.entities as unknown as IChildContact[]).map((c) => c.contactid);

            if (childIds.length === 0) {
                if (!cancelled) { setItems([]); setIsLoading(false); }
                return;
            }

            // Step 2: device registrations where a365_contactid is one of the child contacts
            // Note: Dataverse Web API does not support `in` on _value lookup fields — use OR conditions
            const orFilter = childIds.map((id) => `_a365_contactid_value eq '${id}'`).join(" or ");
            const deviceOptions = `?$select=${DEVICE_SELECT}&$expand=${DEVICE_EXPAND}&$filter=(${orFilter})`;

            const deviceResult = await webAPI.retrieveMultipleRecords(DEVICE_ENTITY, deviceOptions);
            const mapped = (deviceResult.entities as unknown as IDeviceEntity[]).map((e) => ({
                msauto_deviceregistrationid: e.msauto_deviceregistrationid,
                companyName:       e.a365_company?.cdm_name ?? "",
                deviceNumber:      e.msauto_DeviceId?.msauto_devicenumber ?? "",
                deviceName:        e.msauto_DeviceId?.msauto_name ?? "",
                deviceDescription: e.msauto_DeviceId?.msauto_description ?? "",
                brandName:         e.msauto_DeviceId?.msauto_DeviceBrandId?.msauto_name ?? "",
                className:         e.msauto_DeviceId?.msauto_DeviceClassId?.msauto_name ?? "",
                modelName:         e.msauto_DeviceId?.msauto_DeviceModelId?.msauto_name ?? "",
                modelCodeName:     e.msauto_DeviceId?.msauto_DeviceModelCodeId?.msauto_name ?? "",
                configCodeName:    e.msauto_DeviceId?.a365_configurationcodeid?.msauto_name ?? "",
                exteriorName:      e.msauto_DeviceId?.a365_deviceexteriorid?.a365_name ?? "",
                interiorName:      e.msauto_DeviceId?.a365_deviceinteriorid?.a365_name ?? "",
            }));

            if (!cancelled) { setItems(mapped); setIsLoading(false); }
        };

        load().catch((error: Error) => {
            if (!cancelled) {
                setErrorMessage(`Error loading device registrations: ${error.message}`);
                setIsLoading(false);
            }
        });

        return () => { cancelled = true; };
    }, [masterContactId]);

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
