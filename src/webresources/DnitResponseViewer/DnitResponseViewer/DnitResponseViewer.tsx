import * as React from 'react';
import {
    Badge,
    Text,
    Divider,
    makeStyles,
    tokens,
    Accordion,
    AccordionItem,
    AccordionHeader,
    AccordionPanel,
    MessageBar,
    MessageBarBody,
} from '@fluentui/react-components';

// ── Types ──────────────────────────────────────────────────────────────────

interface SetContribuyente {
    razonSocial?: string;
    estado?: string;
    ruc?: string;
    digitoVerificador?: string;
    tipoContribuyente?: string;
    tipoRegimen?: string;
    calle?: string;
    numeroCasa?: string;
    departamento?: string;
    distrito?: string;
    ciudad?: string;
    telefono?: string;
    email?: string;
    fechaInscripcion?: string;
    fechaInicioActividades?: string;
    [key: string]: string | undefined;
}

interface SetRucResponse {
    codigo?: string;
    mensaje?: string;
    estado?: string;
    contribuyente?: SetContribuyente;
}

export interface IDnitResponseViewerProps {
    rawJson: string;
}

// ── Label map (Spanish display names) ─────────────────────────────────────

const LABEL_MAP: Record<string, string> = {
    razonSocial:            'Razón Social',
    ruc:                    'RUC',
    digitoVerificador:      'Dígito Verificador',
    tipoContribuyente:      'Tipo Contribuyente',
    tipoRegimen:            'Tipo Régimen',
    estado:                 'Estado',
    calle:                  'Calle',
    numeroCasa:             'Número',
    departamento:           'Departamento',
    distrito:               'Distrito',
    ciudad:                 'Ciudad',
    telefono:               'Teléfono',
    email:                  'Email',
    fechaInscripcion:       'Fecha Inscripción',
    fechaInicioActividades: 'Inicio Actividades',
};

const FIELD_ORDER = [
    'razonSocial', 'ruc', 'digitoVerificador', 'tipoContribuyente', 'tipoRegimen',
    'estado', 'calle', 'numeroCasa', 'departamento', 'distrito', 'ciudad',
    'telefono', 'email', 'fechaInscripcion', 'fechaInicioActividades',
];

// ── Estado → Badge color ───────────────────────────────────────────────────

type BadgeColor = 'success' | 'warning' | 'danger' | 'informative';

const estadoColor = (estado?: string): BadgeColor => {
    switch ((estado ?? '').toUpperCase()) {
        case 'ACTIVO':               return 'success';
        case 'SUSPENDIDO':
        case 'SUSPENSION TEMPORAL':
        case 'NO VIGENTE':           return 'warning';
        case 'CANCELADO':
        case 'BLOQUEADO':            return 'danger';
        default:                     return 'informative';
    }
};

// ── Styles ─────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    root: {
        padding: '12px',
        fontFamily: tokens.fontFamilyBase,
    },
    header: {
        display: 'flex',
        alignItems: 'center',
        gap: '10px',
        marginBottom: '12px',
    },
    razonSocial: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase400,
        flex: '1',
    },
    grid: {
        display: 'grid',
        gridTemplateColumns: '1fr 1fr',
        gap: '6px 24px',
        marginTop: '10px',
    },
    field: {
        display: 'flex',
        flexDirection: 'column',
        gap: '2px',
    },
    label: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase200,
        textTransform: 'uppercase',
        letterSpacing: '0.04em',
    },
    value: {
        fontSize: tokens.fontSizeBase300,
        color: tokens.colorNeutralForeground1,
    },
    divider: {
        marginBottom: '8px',
    },
    rawJson: {
        fontFamily: tokens.fontFamilyMonospace,
        fontSize: tokens.fontSizeBase200,
        whiteSpace: 'pre-wrap',
        wordBreak: 'break-all',
        backgroundColor: tokens.colorNeutralBackground3,
        padding: '8px',
        borderRadius: tokens.borderRadiusMedium,
        maxHeight: '200px',
        overflowY: 'auto',
    },
    empty: {
        color: tokens.colorNeutralForeground3,
        fontStyle: 'italic',
    },
    topFields: {
        display: 'flex',
        gap: '16px',
        marginBottom: '8px',
        flexWrap: 'wrap',
    },
    topField: {
        display: 'flex',
        flexDirection: 'column',
        gap: '2px',
    },
});

// ── Component ──────────────────────────────────────────────────────────────

export const DnitResponseCard: React.FC<IDnitResponseViewerProps> = ({ rawJson }) => {
    const styles = useStyles();

    if (!rawJson || rawJson.trim() === '') {
        return (
            <div className={styles.root}>
                <Text className={styles.empty}>Sin respuesta de consulta RUC.</Text>
            </div>
        );
    }

    let parsed: SetRucResponse | null = null;
    let parseError = false;

    try {
        parsed = JSON.parse(rawJson) as SetRucResponse;
    } catch {
        parseError = true;
    }

    if (parseError || !parsed) {
        return (
            <div className={styles.root}>
                <MessageBar intent="warning">
                    <MessageBarBody>No se pudo parsear la respuesta JSON.</MessageBarBody>
                </MessageBar>
                <div className={styles.rawJson}>{rawJson}</div>
            </div>
        );
    }

    const c = parsed.contribuyente;
    const estado = c?.estado ?? parsed?.estado;

    // Build ordered field list, skip razonSocial and estado (shown in header)
    const orderedKeys = [
        ...FIELD_ORDER.filter(k => k !== 'razonSocial' && k !== 'estado'),
        ...Object.keys(c ?? {}).filter(k => !FIELD_ORDER.includes(k) && k !== 'razonSocial' && k !== 'estado'),
    ];

    const fieldsToRender = orderedKeys.filter(k => c?.[k]);

    return (
        <div className={styles.root}>
            {/* Header: razón social + estado badge */}
            <div className={styles.header}>
                <Text className={styles.razonSocial}>{c?.razonSocial ?? '—'}</Text>
                {estado && (
                    <Badge
                        appearance="filled"
                        color={estadoColor(estado)}
                        size="medium"
                    >
                        {estado}
                    </Badge>
                )}
            </div>

            {/* Top-level API status */}
            {(parsed.codigo ?? parsed.mensaje) && (
                <>
                    <div className={styles.topFields}>
                        {parsed.codigo && (
                            <div className={styles.topField}>
                                <Text className={styles.label}>Código</Text>
                                <Text className={styles.value}>{parsed.codigo}</Text>
                            </div>
                        )}
                        {parsed.mensaje && (
                            <div className={styles.topField}>
                                <Text className={styles.label}>Mensaje</Text>
                                <Text className={styles.value}>{parsed.mensaje}</Text>
                            </div>
                        )}
                    </div>
                    <Divider className={styles.divider} />
                </>
            )}

            {/* Contribuyente fields */}
            {c && fieldsToRender.length > 0 && (
                <div className={styles.grid}>
                    {fieldsToRender.map(key => (
                        <div key={key} className={styles.field}>
                            <Text className={styles.label}>{LABEL_MAP[key] ?? key}</Text>
                            <Text className={styles.value}>{c[key] ?? '—'}</Text>
                        </div>
                    ))}
                </div>
            )}

            {/* Raw JSON collapsible */}
            <Accordion collapsible>
                <AccordionItem value="raw">
                    <AccordionHeader size="small">JSON completo</AccordionHeader>
                    <AccordionPanel>
                        <div className={styles.rawJson}>
                            {JSON.stringify(parsed, null, 2)}
                        </div>
                    </AccordionPanel>
                </AccordionItem>
            </Accordion>
        </div>
    );
};
