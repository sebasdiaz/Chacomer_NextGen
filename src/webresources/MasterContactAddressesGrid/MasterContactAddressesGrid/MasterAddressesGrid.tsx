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

const SELECT_FIELDS =
    'customeraddressid,addressnumber,addresstypecode,line1,axx_numero,msdyn_streetnumber,city,stateorprovince,country';

// El padre del domicilio es un lookup polimorfico (account | contact). parentid_contact es
// la navigation property hacia contacto, y permite resolver todo en UNA query: no hace falta
// traer primero los hijos y despues filtrar por una lista de GUIDs.
const EXPAND_FIELDS = 'parentid_contact($select=contactid,fullname)';

// Dataverse crea automaticamente los customeraddress 1 y 2 de cada contacto, y en este
// environment la mayoria nunca se completa: sobre 422 registros de contactos hijo de un
// master, 337 no tienen ningun dato. Sin esta guarda la grilla muestra mayormente filas
// en blanco. Se filtra en el servidor para no traer lo que no se va a mostrar.
const NON_EMPTY_FIELDS = [
    'line1', 'line2', 'line3', 'city', 'county',
    'stateorprovince', 'postalcode', 'country', 'axx_numero', 'msdyn_streetnumber', 'name',
];
const NON_EMPTY_FILTER = `(${NON_EMPTY_FIELDS.map((f) => `${f} ne null`).join(' or ')})`;

const FORMATTED_VALUE = '@OData.Community.Display.V1.FormattedValue';

// Fallback por si la anotacion de formatted value no viene. Valores verificados contra la
// metadata del environment.
const ADDRESS_TYPE_LABELS: Record<number, string> = {
    1: 'Facturacion',
    2: 'Envio',
    3: 'Principal',
    4: 'Otro',
};

// context.page.entityId normalmente devuelve el GUID pelado, pero segun el host puede venir
// entre llaves. Se normaliza y se valida antes de interpolarlo en el $filter: un GUID mal
// formado se traduce en un 400 de Dataverse que no explica cual es el problema.
const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function normalizeGuid(value: string | null): string | null {
    if (!value) return null;
    const trimmed = value.trim().replace(/^\{/, '').replace(/\}$/, '');
    return GUID_PATTERN.test(trimmed) ? trimmed : null;
}

interface ICustomerAddressEntity {
    customeraddressid: string;
    addressnumber?: number;
    addresstypecode?: number;
    line1?: string;
    axx_numero?: string;
    msdyn_streetnumber?: string;
    city?: string;
    stateorprovince?: string;
    country?: string;
    parentid_contact?: { contactid?: string; fullname?: string };
    [annotation: string]: unknown;
}

interface IMasterAddress {
    customeraddressid: string;
    contactName: string;
    addressNumber: number;
    addressType: string;
    line1: string;
    numero: string;
    city: string;
    stateOrProvince: string;
    country: string;
}

export interface IMasterAddressesGridProps {
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
    contactName: { idealWidth: 190, minWidth: 130 },
    addressType: { idealWidth: 100, minWidth: 80 },
    line1: { idealWidth: 240, minWidth: 150 },
    numero: { idealWidth: 80, minWidth: 60 },
    city: { idealWidth: 150, minWidth: 100 },
    stateOrProvince: { idealWidth: 150, minWidth: 100 },
    country: { idealWidth: 120, minWidth: 90 },
};

const columns: TableColumnDefinition<IMasterAddress>[] = [
    createTableColumn<IMasterAddress>({
        columnId: 'contactName',
        renderHeaderCell: () => 'Contacto',
        renderCell: (item) => item.contactName || '—',
    }),
    createTableColumn<IMasterAddress>({
        columnId: 'addressType',
        renderHeaderCell: () => 'Tipo',
        renderCell: (item) => item.addressType || '—',
    }),
    createTableColumn<IMasterAddress>({
        columnId: 'line1',
        renderHeaderCell: () => 'Calle',
        renderCell: (item) => item.line1 || '—',
    }),
    createTableColumn<IMasterAddress>({
        columnId: 'numero',
        renderHeaderCell: () => 'Nro.',
        renderCell: (item) => item.numero || '—',
    }),
    createTableColumn<IMasterAddress>({
        columnId: 'city',
        renderHeaderCell: () => 'Ciudad',
        renderCell: (item) => item.city || '—',
    }),
    createTableColumn<IMasterAddress>({
        columnId: 'stateOrProvince',
        renderHeaderCell: () => 'Departamento',
        renderCell: (item) => item.stateOrProvince || '—',
    }),
    createTableColumn<IMasterAddress>({
        columnId: 'country',
        renderHeaderCell: () => 'Pais',
        renderCell: (item) => item.country || '—',
    }),
];

function resolveAddressType(entity: ICustomerAddressEntity): string {
    const formatted = entity[`addresstypecode${FORMATTED_VALUE}`];
    if (typeof formatted === 'string' && formatted.length > 0) return formatted;
    return entity.addresstypecode !== undefined
        ? (ADDRESS_TYPE_LABELS[entity.addresstypecode] ?? '')
        : '';
}

export const MasterAddressesGrid: React.FC<IMasterAddressesGridProps> = ({ masterContactId, webAPI }) => {
    const styles = useStyles();
    const contactId = React.useMemo(() => normalizeGuid(masterContactId), [masterContactId]);
    const [addresses, setAddresses] = React.useState<IMasterAddress[]>([]);
    // Arranca cargando cuando hay un master que consultar: si arrancara en false, el primer
    // render mostraria el mensaje de "sin domicilios" hasta que resuelva la query.
    const [isLoading, setIsLoading] = React.useState(() => contactId !== null);
    const [errorMessage, setErrorMessage] = React.useState<string | null>(null);

    React.useEffect(() => {
        if (!contactId) {
            setAddresses([]);
            setIsLoading(false);
            return;
        }

        let cancelled = false;
        setIsLoading(true);
        setErrorMessage(null);

        const filter =
            `parentid_contact/_axx_mastercontactid_value eq ${contactId} and ${NON_EMPTY_FILTER}`;
        const options =
            `?$select=${SELECT_FIELDS}&$expand=${EXPAND_FIELDS}&$filter=${filter}&$orderby=addressnumber`;

        webAPI.retrieveMultipleRecords('customeraddress', options)
            .then((result) => {
                if (cancelled) return undefined;

                const mapped = (result.entities as unknown as ICustomerAddressEntity[]).map((e) => ({
                    customeraddressid: e.customeraddressid,
                    contactName: e.parentid_contact?.fullname ?? '',
                    addressNumber: e.addressnumber ?? 0,
                    addressType: resolveAddressType(e),
                    line1: e.line1 ?? '',
                    // El numero de calle vive en dos campos segun quien lo cargo: los
                    // formularios de este environment llenan msdyn_streetnumber y
                    // axx_numero queda vacio. Se muestra el que tenga dato.
                    numero: e.axx_numero ?? e.msdyn_streetnumber ?? '',
                    city: e.city ?? '',
                    stateOrProvince: e.stateorprovince ?? '',
                    country: e.country ?? '',
                }));

                // Se agrupa visualmente por contacto. El $orderby no puede ordenar por un
                // campo de la entidad relacionada (Dataverse no lo soporta), asi que el
                // orden por nombre se resuelve aca y el servidor solo ordena por numero.
                mapped.sort((a, b) => {
                    const byContact = a.contactName.localeCompare(b.contactName, 'es');
                    return byContact !== 0 ? byContact : a.addressNumber - b.addressNumber;
                });

                setAddresses(mapped);
                setIsLoading(false);
                return undefined;
            })
            .catch((error: Error) => {
                if (cancelled) return;
                setErrorMessage(`Error al cargar los domicilios: ${error.message}`);
                setIsLoading(false);
            });

        return () => { cancelled = true; };
    }, [contactId]);

    if (isLoading) {
        return (
            <div className={styles.spinnerWrapper}>
                <Spinner label="Cargando domicilios..." size="small" />
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

    if (addresses.length === 0) {
        return (
            <div className={styles.container}>
                <span className={styles.emptyMessage}>
                    Los contactos hijo de este master no tienen domicilios cargados.
                </span>
            </div>
        );
    }

    return (
        <div className={styles.container}>
            <DataGrid
                items={addresses}
                columns={columns}
                getRowId={(item: IMasterAddress) => item.customeraddressid}
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
                <DataGridBody<IMasterAddress>>
                    {({ item, rowId }) => (
                        <DataGridRow<IMasterAddress> key={rowId}>
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
