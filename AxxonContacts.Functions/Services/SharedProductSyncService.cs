using AxxonContacts.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonContacts.Functions.Services
{
    /// <summary>
    /// Sincroniza productos liberados de F&O hacia msdyn_sharedproductdetails en Dataverse.
    ///
    /// Estrategia de upsert:
    ///   1. Resuelve los lookups necesarios (msdyn_globalproducts, uoms, msdyn_vendors, etc.)
    ///      usando caches en memoria validos durante la ejecucion del sync.
    ///   2. Resuelve msdyn_companyid via cdm_company.cdm_companycode = dataAreaId.
    ///   3. Para cada producto, busca el registro existente por (msdyn_itemnumber, msdyn_companyid).
    ///      - Si existe: Update.
    ///      - Si no existe: Create.
    ///   4. Procesa en batches de ExecuteMultiple para reducir round-trips.
    ///
    /// Value maps corresponden 1-a-1 con el archivo ReleaseProductV2_Mapping.json.
    /// </summary>
    public class SharedProductSyncService : ISharedProductSyncService
    {
        private const string EntityName   = "msdyn_sharedproductdetails";
        private const int    BatchSize    = 200;

        private readonly IOrganizationService _orgService;
        private readonly ILogger<SharedProductSyncService> _logger;

        // Caches de lookups (validos durante una ejecucion del timer)
        private readonly Dictionary<string, Guid> _globalProductCache  = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid> _uomCache            = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid> _vendorCache         = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid> _prodDimGroupCache   = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid> _storageDimGroupCache= new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid> _trackingDimGroupCache=new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid> _companyCache        = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid> _altItemCache        = new(StringComparer.OrdinalIgnoreCase);

        public SharedProductSyncService(IOrganizationService orgService, ILogger<SharedProductSyncService> logger)
        {
            _orgService = orgService;
            _logger     = logger;
        }

        public async Task SyncAsync(IReadOnlyList<FoReleasedProduct> products, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("[SharedProductSyncService] Iniciando sync de {Count} productos.", products.Count);

            var created = 0;
            var updated = 0;
            var failed  = 0;

            for (var i = 0; i < products.Count; i += BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = products.Skip(i).Take(BatchSize).ToList();
                _logger.LogInformation(
                    "[SharedProductSyncService] Procesando batch {From}-{To} de {Total}.",
                    i + 1, Math.Min(i + BatchSize, products.Count), products.Count);

                var requests = new ExecuteMultipleRequest
                {
                    Requests = new OrganizationRequestCollection(),
                    Settings = new ExecuteMultipleSettings
                    {
                        ContinueOnError = true,
                        ReturnResponses = true
                    }
                };

                foreach (var product in batch)
                {
                    try
                    {
                        var entity = await MapToEntityAsync(product, cancellationToken);
                        var existingId = FindExisting(product.ItemNumber, product.DataAreaId);

                        if (existingId.HasValue)
                        {
                            entity.Id = existingId.Value;
                            requests.Requests.Add(new UpdateRequest { Target = entity });
                        }
                        else
                        {
                            requests.Requests.Add(new CreateRequest { Target = entity });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "[SharedProductSyncService] Error mapeando producto ItemNumber={Item} DataAreaId={Area}. Se omite.",
                            product.ItemNumber, product.DataAreaId);
                        failed++;
                    }
                }

                if (requests.Requests.Count == 0)
                    continue;

                var multiResponse = (ExecuteMultipleResponse)_orgService.Execute(requests);

                foreach (var responseItem in multiResponse.Responses)
                {
                    if (responseItem.Fault is not null)
                    {
                        _logger.LogError(
                            "[SharedProductSyncService] Fault en request index {Index}: {Message}",
                            responseItem.RequestIndex, responseItem.Fault.Message);
                        failed++;
                    }
                    else if (responseItem.Response is CreateResponse)
                        created++;
                    else
                        updated++;
                }
            }

            _logger.LogInformation(
                "[SharedProductSyncService] Sync completado. Creados={Created} Actualizados={Updated} Fallidos={Failed}",
                created, updated, failed);
        }

        private async Task<Entity> MapToEntityAsync(FoReleasedProduct p, CancellationToken ct)
        {
            var e = new Entity(EntityName);

            e["msdyn_itemnumber"] = p.ItemNumber;

            // --- Lookup: msdyn_companyid via cdm_company.cdm_companycode = dataAreaId ---
            var companyRef = await ResolveLookupAsync(
                _companyCache, p.DataAreaId, "cdm_company", "cdm_companyid", "cdm_companycode", ct);
            if (companyRef.HasValue)
                e["msdyn_companyid"] = new EntityReference("cdm_company", companyRef.Value);

            // --- Lookup: msdyn_globalproduct via msdyn_globalproducts.msdyn_productnumber ---
            if (!string.IsNullOrEmpty(p.ProductNumber))
            {
                var gpRef = await ResolveLookupAsync(
                    _globalProductCache, p.ProductNumber, "msdyn_globalproduct", "msdyn_globalproductid", "msdyn_productnumber", ct);
                if (gpRef.HasValue)
                    e["msdyn_globalproduct"] = new EntityReference("msdyn_globalproduct", gpRef.Value);
            }

            // --- Scalars ---
            SetIfNotNull(e, "msdyn_intrastatchargepercentage",                p.IntrastatChargePercentage);
            SetIfNotNull(e, "msdyn_approximatesalestaxpercentage",             p.ApproximateSalesTaxPercentage);
            SetIfNotNull(e, "msdyn_bestbeforeperioddays",                      p.BestBeforePeriodDays);
            SetIfNotNull(e, "msdyn_constantscrapquantity",                     p.ConstantScrapQuantity);
            SetIfNotNull(e, "msdyn_costchargesquantity",                       p.CostChargesQuantity);
            SetIfNotNull(e, "msdyn_defaultreceivingquantity",                  p.DefaultReceivingQuantity);
            SetIfNotNull(e, "msdyn_fixedpurchasepricecharges",                 p.FixedPurchasePriceCharges);
            SetIfNotNull(e, "msdyn_fixedsalespricecharges",                    p.FixedSalesPriceCharges);
            SetIfNotNull(e, "msdyn_grossdepth",                                p.GrossDepth);
            SetIfNotNull(e, "msdyn_grossproductheight",                        p.GrossProductHeight);
            SetIfNotNull(e, "msdyn_grossproductwidth",                         p.GrossProductWidth);
            SetIfNotNull(e, "msdyn_maximumpickquantity",                       p.MaximumPickQuantity);
            SetIfNotNull(e, "msdyn_necessaryproductionworkingtimeschedulingp",  p.NecessaryProductionWorkingTimeSchedulingPropertyId);
            SetIfNotNull(e, "msdyn_netproductweight",                          p.NetProductWeight);
            SetIfNotNull(e, "msdyn_packingdutyquantity",                       p.PackingDutyQuantity);
            SetIfNotNull(e, "msdyn_posregistrationactivationdate",             p.PosRegistrationActivationDate?.UtcDateTime);
            SetIfNotNull(e, "msdyn_posregistrationblockeddate",                p.PosRegistrationBlockedDate?.UtcDateTime);
            SetIfNotNull(e, "msdyn_posregistrationplannedblockeddate",         p.PosRegistrationPlannedBlockedDate?.UtcDateTime);
            SetIfNotNull(e, "msdyn_potencybaseattibutetargetvalue",            p.PotencyBaseAttributeTargetValue);
            SetIfNotNull(e, "msdyn_productionconsumptiondensityconversion",    p.ProductionConsumptionDensityConversionFactor);
            SetIfNotNull(e, "msdyn_productionconsumptiondepthconversion",      p.ProductionConsumptionDepthConversionFactor);
            SetIfNotNull(e, "msdyn_productionconsumptionheightconversion",     p.ProductionConsumptionHeightConversionFactor);
            SetIfNotNull(e, "msdyn_productionconsumptionwidthconversion",      p.ProductionConsumptionWidthConversionFactor);
            SetIfNotNull(e, "msdyn_productvolume",                             p.ProductVolume);
            SetIfNotNull(e, "msdyn_purchasechargesquantity",                   p.PurchaseChargesQuantity);
            SetIfNotNull(e, "msdyn_purchaseoverdeliverypercentage",            p.PurchaseOverDeliveryPercentage);
            SetIfNotNull(e, "msdyn_purchaseprice",                             p.PurchasePrice);
            SetIfNotNull(e, "msdyn_purchasepricedate",                         p.PurchasePriceDate?.UtcDateTime);
            SetIfNotNull(e, "msdyn_purchasepricingprecision",                  p.PurchasePricingPrecision);
            SetIfNotNull(e, "msdyn_purchaseunderdeliverypercentage",           p.PurchaseUnderDeliveryPercentage);
            SetIfNotNull(e, "msdyn_saleschargesquantity",                      p.SalesChargesQuantity);
            SetIfNotNull(e, "msdyn_salesoverdeliverypercentage",               p.SalesOverDeliveryPercentage);
            SetIfNotNull(e, "msdyn_salesprice",                                p.SalesPrice);
            SetIfNotNull(e, "msdyn_salespricecalculationchargespercentage",    p.SalesPriceCalculationChargesPercentage);
            SetIfNotNull(e, "msdyn_salespricecalculationcontributionratio",    p.SalesPriceCalculationContributionRatio);
            SetIfNotNull(e, "msdyn_salespricedate",                            p.SalesPriceDate?.UtcDateTime);
            SetIfNotNull(e, "msdyn_salespricingprecision",                     p.SalesPricingPrecision);
            SetIfNotNull(e, "msdyn_salesunderdeliverypercentage",              p.SalesUnderDeliveryPercentage);
            SetIfNotNull(e, "msdyn_sellstartdate",                             p.SellStartDate?.UtcDateTime);
            SetIfNotNull(e, "msdyn_shelfadviceperioddays",                     p.ShelfAdvicePeriodDays);
            SetIfNotNull(e, "msdyn_shelflifeperioddays",                       p.ShelfLifePeriodDays);
            SetIfNotNull(e, "msdyn_shipstartdate",                             p.ShipStartDate?.UtcDateTime);
            SetIfNotNull(e, "msdyn_tareproductweight",                         p.TareProductWeight);
            SetIfNotNull(e, "msdyn_transferorderoverdeliverypercentage",       p.TransferOrderOverDeliveryPercentage);
            SetIfNotNull(e, "msdyn_transferorderunderdeliverypercentage",      p.TransferOrderUnderDeliveryPercentage);
            SetIfNotNull(e, "msdyn_unitcost",                                  p.UnitCost);
            SetIfNotNull(e, "msdyn_unitcostdate",                              p.UnitCostDate?.UtcDateTime);
            SetIfNotNull(e, "msdyn_unitcostquantity",                          p.UnitCostQuantity);
            SetIfNotNull(e, "msdyn_variablescrappercentage",                   p.VariableScrapPercentage);
            SetIfNotNull(e, "msdyn_warehousemobiledevicedescriptionline1",     p.WarehouseMobileDeviceDescriptionLine1);
            SetIfNotNull(e, "msdyn_warehousemobiledevicedescriptionline2",     p.WarehouseMobileDeviceDescriptionLine2);
            SetIfNotNull(e, "msdyn_yieldpercentage",                           p.YieldPercentage);
            SetIfNotNull(e, "msdyn_purchasepricequantity",                     p.PurchasePriceQuantity);
            SetIfNotNull(e, "msdyn_fixedcostcharges",                          p.FixedCostCharges);
            SetIfNotNull(e, "msdyn_minimumcatchweightquantity",                p.MinimumCatchWeightQuantity);
            SetIfNotNull(e, "msdyn_maximumcatchweightquantity",                p.MaximumCatchWeightQuantity);
            SetIfNotNull(e, "msdyn_alternativeitemnumber",                     p.AlternativeItemNumber);

            // msdyn_shouldsyncwithproducts: Default = False (segun mapping)
            e["msdyn_shouldsyncwithproducts"] = false;

            // --- OptionSets (value maps del JSON) ---
            MapOptionSet(e, "msdyn_carryingcostabccode", p.CarryingCostAbcCode,
                ("a", 806380001), ("b", 806380002), ("c", 806380003), ("none", 806380000));

            MapOptionSet(e, "msdyn_marginabccode", p.MarginAbcCode,
                ("a", 806380001), ("b", 806380002), ("c", 806380003), ("none", 806380000));

            MapOptionSet(e, "msdyn_keyinpricerequirementsatposregister", p.KeyInPriceRequirementsAtPosRegister,
                ("higherEqual", 806380002), ("lowerEqual", 806380003), ("newPrice", 806380001), ("noPrice", 806380004), ("notMandatory", 806380000));

            MapOptionSet(e, "msdyn_keyinquantityrequirementsatposregister", p.KeyInQuantityRequirementsAtPosRegister,
                ("keyIn", 806380001), ("notKeyIn", 806380002), ("notMandatory", 806380000));

            MapOptionSet(e, "msdyn_potencybaseattributevalueentryevent", p.PotencyBaseAttributeValueEntryEvent,
                ("purchProdReceipt", 806380000), ("quality", 806380001));

            MapOptionSet(e, "msdyn_producttype", p.ProductType,
                ("item", 806380001), ("service", 806380002));

            MapOptionSet(e, "msdyn_rawmaterialpickingprinciple", p.RawMaterialPickingPrinciple,
                ("orderPicking", 806380001), ("staging", 806380000));

            MapOptionSet(e, "msdyn_salespricecalculationmodel", p.SalesPriceCalculationModel,
                ("contributionratio", 806380001), ("none", 806380000), ("percentMarkup", 806380002));

            MapOptionSet(e, "msdyn_scaleindicator", p.ScaleIndicator,
                ("notRelevant", 806380001), ("relevant", 806380000));

            // --- Booleans (yes/no -> True/False) ---
            MapBool(e, "msdyn_isdiscountposregistrationprohibited",         p.IsDiscountPosRegistrationProhibited);
            MapBool(e, "msdyn_exemptautomaticnotificationcancel",            p.IsExemptFromAutomaticNotificationAndCancellation);
            MapBool(e, "msdyn_isinstallmenteligible",                        p.IsInstallmentEligible);
            MapBool(e, "msdyn_isintercompanypurchaseusageblocked",           p.IsIntercompanyPurchaseUsageBlocked);
            MapBool(e, "msdyn_isintercompanysalesusageblocked",              p.IsIntercompanySalesUsageBlocked);
            MapBool(e, "msdyn_ismanualdiscposregistrationprohibited",        p.IsManualDiscountPosRegistrationProhibited);
            MapBool(e, "msdyn_isphantom",                                    p.IsPhantom);
            MapBool(e, "msdyn_isposregistrationblocked",                     p.IsPosRegistrationBlocked);
            MapBool(e, "msdyn_isposregistrationquantitynegative",            p.IsPosRegistrationQuantityNegative);
            MapBool(e, "msdyn_ispurchasepriceautomaticallyupdated",          p.IsPurchasePriceAutomaticallyUpdated);
            MapBool(e, "msdyn_ispurchasepriceincludingcharges",              p.IsPurchasePriceIncludingCharges);
            MapBool(e, "msdyn_issaleswithholdingtaxcalculated",              p.IsSalesWithholdingTaxCalculated);
            MapBool(e, "msdyn_isrestrictedforcoupons",                       p.IsRestrictedForCoupons);
            MapBool(e, "msdyn_issalespriceadjustmentallowed",                p.IsSalesPriceAdjustmentAllowed);
            MapBool(e, "msdyn_issalespriceincludingcharges",                 p.IsSalesPriceIncludingCharges);
            MapBool(e, "msdyn_isscaleproduct",                               p.IsScaleProduct);
            MapBool(e, "msdyn_isshipaloneenabled",                           p.IsShipAloneEnabled);
            MapBool(e, "msdyn_isunitcostproductvariantspecific",             p.IsUnitCostProductVariantSpecific);
            MapBool(e, "msdyn_isvariantshelflabelsprintingenabled",          p.IsVariantShelfLabelsPrintingEnabled);
            MapBool(e, "msdyn_iszeropriceposregistrationallowed",            p.IsZeroPricePosRegistrationAllowed);
            MapBool(e, "msdyn_mustkeyincommentatposregister",                p.MustKeyInCommentAtPosRegister);
            MapBool(e, "msdyn_willinventoryissueautoreportasfinished",       p.WillInventoryIssueAutomaticallyReportAsFinished);
            MapBool(e, "msdyn_willinventoryreceiptignoreflushing",           p.WillInventoryReceiptIgnoreFlushingPrinciple);
            MapBool(e, "msdyn_willpickingworkbenchapplyboxinglogic",         p.WillPickingWorkbenchApplyBoxingLogic);
            MapBool(e, "msdyn_willtotalpurchdiscountcalcincludeproduct",     p.WillTotalPurchaseDiscountCalculationIncludeProduct);
            MapBool(e, "msdyn_willtotalsalesdiscountcalcincludeproduct",     p.WillTotalSalesDiscountCalculationIncludeProduct);
            MapBool(e, "msdyn_willworkcenterpickingallownegativeinvent",     p.WillWorkCenterPickingAllowNegativeInventory);
            MapBool(e, "msdyn_isunitcostautomaticallyupdated",               p.IsUnitCostAutomaticallyUpdated);
            MapBool(e, "msdyn_isunitcostincludingcharges",                   p.IsUnitCostIncludingCharges);
            MapBool(e, "msdyn_iscatchweight",                                p.IsCatchWeightProduct);

            // --- Lookups: UoM ---
            await MapUomAsync(e, "msdyn_inventoryunitsymbol", p.InventoryUnitSymbol, ct);
            await MapUomAsync(e, "msdyn_salesunitsymbol",     p.SalesUnitSymbol,     ct);
            await MapUomAsync(e, "msdyn_purchaseunitsymbol",  p.PurchaseUnitSymbol,  ct);
            await MapUomAsync(e, "msdyn_bomunitsymbol",       p.BomUnitSymbol,       ct);
            await MapUomAsync(e, "msdyn_catchweightunitsymbol",         p.CatchWeightUnitSymbol,          ct);
            await MapUomAsync(e, "msdyn_comparisonpricebaseunitsymbol", p.ComparisonPriceBaseUnitSymbol,  ct);

            // --- Lookup: Vendor ---
            if (!string.IsNullOrEmpty(p.PrimaryVendorAccountNumber))
            {
                var vRef = await ResolveLookupAsync(
                    _vendorCache, p.PrimaryVendorAccountNumber,
                    "msdyn_vendor", "msdyn_vendorid", "msdyn_vendoraccountnumber", ct);
                if (vRef.HasValue)
                    e["msdyn_vendorid"] = new EntityReference("msdyn_vendor", vRef.Value);
            }

            // --- Lookups: dimension groups ---
            if (!string.IsNullOrEmpty(p.ProductDimensionGroupName))
            {
                var r = await ResolveLookupAsync(_prodDimGroupCache, p.ProductDimensionGroupName,
                    "msdyn_productdimensiongroup", "msdyn_productdimensiongroupid", "msdyn_groupname", ct);
                if (r.HasValue)
                    e["msdyn_productdimensiongroupid"] = new EntityReference("msdyn_productdimensiongroup", r.Value);
            }

            if (!string.IsNullOrEmpty(p.StorageDimensionGroupName))
            {
                var r = await ResolveLookupAsync(_storageDimGroupCache, p.StorageDimensionGroupName,
                    "msdyn_productstoragedimensiongroup", "msdyn_productstoragedimensiongroupid", "msdyn_groupname", ct);
                if (r.HasValue)
                    e["msdyn_storagedimensiongroup"] = new EntityReference("msdyn_productstoragedimensiongroup", r.Value);
            }

            if (!string.IsNullOrEmpty(p.TrackingDimensionGroupName))
            {
                var r = await ResolveLookupAsync(_trackingDimGroupCache, p.TrackingDimensionGroupName,
                    "msdyn_producttrackingdimensiongroup", "msdyn_producttrackingdimensiongroupid", "msdyn_groupname", ct);
                if (r.HasValue)
                    e["msdyn_trackingdimensiongroup"] = new EntityReference("msdyn_producttrackingdimensiongroup", r.Value);
            }

            // --- Lookup: msdyn_alternativeitemnumber ---
            if (!string.IsNullOrEmpty(p.AlternativeItemNumber))
            {
                var r = await ResolveLookupAsync(_altItemCache, p.AlternativeItemNumber,
                    "msdyn_sharedproductdetails", "msdyn_sharedproductdetailsid", "msdyn_itemnumber", ct);
                if (r.HasValue)
                    e["msdyn_alternativeitemnumber"] = new EntityReference("msdyn_sharedproductdetails", r.Value);
            }

            return e;
        }

        private Guid? FindExisting(string itemNumber, string dataAreaId)
        {
            try
            {
                var query = new QueryExpression(EntityName)
                {
                    ColumnSet    = new ColumnSet(false),
                    TopCount     = 1,
                    NoLock       = true
                };

                query.Criteria.AddCondition("msdyn_itemnumber", ConditionOperator.Equal, itemNumber);

                // Join a cdm_company para filtrar por companycode
                var companyLink = query.AddLink("cdm_company", "msdyn_companyid", "cdm_companyid");
                companyLink.LinkCriteria.AddCondition("cdm_companycode", ConditionOperator.Equal, dataAreaId);

                var result = _orgService.RetrieveMultiple(query);
                return result.Entities.Count > 0 ? result.Entities[0].Id : (Guid?)null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[SharedProductSyncService] Error buscando existente para ItemNumber={Item} DataArea={Area}.",
                    itemNumber, dataAreaId);
                return null;
            }
        }

        private async Task<Guid?> ResolveLookupAsync(
            Dictionary<string, Guid> cache,
            string key,
            string entityName,
            string idField,
            string keyField,
            CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            if (cache.TryGetValue(key, out var cached))
                return cached;

            await Task.Yield(); // mantiene la firma async sin bloquear

            try
            {
                var query = new QueryExpression(entityName)
                {
                    ColumnSet = new ColumnSet(idField),
                    TopCount  = 1,
                    NoLock    = true
                };
                query.Criteria.AddCondition(keyField, ConditionOperator.Equal, key);

                var result = _orgService.RetrieveMultiple(query);

                if (result.Entities.Count == 0)
                {
                    _logger.LogWarning(
                        "[SharedProductSyncService] Lookup no encontrado. Entity={Entity} {Field}={Key}",
                        entityName, keyField, key);
                    return null;
                }

                var id = result.Entities[0].Id;
                cache[key] = id;
                return id;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[SharedProductSyncService] Error resolviendo lookup {Entity}.{Field}={Key}.",
                    entityName, keyField, key);
                return null;
            }
        }

        private async Task MapUomAsync(Entity e, string field, string? symbol, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(symbol))
                return;

            var r = await ResolveLookupAsync(_uomCache, symbol, "uom", "uomid", "name", ct);
            if (r.HasValue)
                e[field] = new EntityReference("uom", r.Value);
        }

        private static void SetIfNotNull(Entity e, string field, object? value)
        {
            if (value is not null)
                e[field] = value;
        }

        private static void MapBool(Entity e, string field, string? foValue)
        {
            if (string.IsNullOrEmpty(foValue))
                return;

            e[field] = foValue.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static void MapOptionSet(Entity e, string field, string? foValue, params (string Key, int Value)[] map)
        {
            if (string.IsNullOrEmpty(foValue))
                return;

            foreach (var (key, value) in map)
            {
                if (foValue.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    e[field] = new OptionSetValue(value);
                    return;
                }
            }
        }
    }
}
