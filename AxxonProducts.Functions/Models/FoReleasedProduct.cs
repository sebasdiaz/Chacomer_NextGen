using System.Text.Json.Serialization;

namespace AxxonProducts.Functions.Models
{
    /// <summary>
    /// DTO que representa un registro de la entidad ReleasedProductsV2 de F&O.
    /// Campos mapeados segun ReleaseProductV2_Mapping.json (cross-company query incluye dataAreaId).
    /// </summary>
    public class FoReleasedProduct
    {
        [JsonPropertyName("dataAreaId")]
        public string DataAreaId { get; set; } = string.Empty;

        [JsonPropertyName("PRODUCTNUMBER")]
        public string ProductNumber { get; set; } = string.Empty;

        [JsonPropertyName("ITEMNUMBER")]
        public string ItemNumber { get; set; } = string.Empty;

        [JsonPropertyName("INTRASTATCHARGEPERCENTAGE")]
        public decimal? IntrastatChargePercentage { get; set; }

        [JsonPropertyName("APPROXIMATESALESTAXPERCENTAGE")]
        public decimal? ApproximateSalesTaxPercentage { get; set; }

        [JsonPropertyName("BESTBEFOREPERIODDAYS")]
        public int? BestBeforePeriodDays { get; set; }

        [JsonPropertyName("CARRYINGCOSTABCCODE")]
        public string? CarryingCostAbcCode { get; set; }

        [JsonPropertyName("CONSTANTSCRAPQUANTITY")]
        public decimal? ConstantScrapQuantity { get; set; }

        [JsonPropertyName("COSTCHARGESQUANTITY")]
        public decimal? CostChargesQuantity { get; set; }

        [JsonPropertyName("DEFAULTRECEIVINGQUANTITY")]
        public decimal? DefaultReceivingQuantity { get; set; }

        [JsonPropertyName("FIXEDPURCHASEPRICECHARGES")]
        public decimal? FixedPurchasePriceCharges { get; set; }

        [JsonPropertyName("FIXEDSALESPRICECHARGES")]
        public decimal? FixedSalesPriceCharges { get; set; }

        [JsonPropertyName("GROSSDEPTH")]
        public decimal? GrossDepth { get; set; }

        [JsonPropertyName("GROSSPRODUCTHEIGHT")]
        public decimal? GrossProductHeight { get; set; }

        [JsonPropertyName("GROSSPRODUCTWIDTH")]
        public decimal? GrossProductWidth { get; set; }

        [JsonPropertyName("INVENTORYUNITSYMBOL")]
        public string? InventoryUnitSymbol { get; set; }

        [JsonPropertyName("ISDISCOUNTPOSREGISTRATIONPROHIBITED")]
        public string? IsDiscountPosRegistrationProhibited { get; set; }

        [JsonPropertyName("ISEXEMPTFROMAUTOMATICNOTIFICATIONANDCANCELLATION")]
        public string? IsExemptFromAutomaticNotificationAndCancellation { get; set; }

        [JsonPropertyName("ISINSTALLMENTELIGIBLE")]
        public string? IsInstallmentEligible { get; set; }

        [JsonPropertyName("ISINTERCOMPANYPURCHASEUSAGEBLOCKED")]
        public string? IsIntercompanyPurchaseUsageBlocked { get; set; }

        [JsonPropertyName("ISINTERCOMPANYSALESUSAGEBLOCKED")]
        public string? IsIntercompanySalesUsageBlocked { get; set; }

        [JsonPropertyName("ISMANUALDISCOUNTPOSREGISTRATIONPROHIBITED")]
        public string? IsManualDiscountPosRegistrationProhibited { get; set; }

        [JsonPropertyName("ISPHANTOM")]
        public string? IsPhantom { get; set; }

        [JsonPropertyName("ISPOSREGISTRATIONBLOCKED")]
        public string? IsPosRegistrationBlocked { get; set; }

        [JsonPropertyName("ISPOSREGISTRATIONQUANTITYNEGATIVE")]
        public string? IsPosRegistrationQuantityNegative { get; set; }

        [JsonPropertyName("ISPURCHASEPRICEAUTOMATICALLYUPDATED")]
        public string? IsPurchasePriceAutomaticallyUpdated { get; set; }

        [JsonPropertyName("ISPURCHASEPRICEINCLUDINGCHARGES")]
        public string? IsPurchasePriceIncludingCharges { get; set; }

        [JsonPropertyName("ISSALESWITHHOLDINGTAXCALCULATED")]
        public string? IsSalesWithholdingTaxCalculated { get; set; }

        [JsonPropertyName("ISRESTRICTEDFORCOUPONS")]
        public string? IsRestrictedForCoupons { get; set; }

        [JsonPropertyName("ISSALESPRICEADJUSTMENTALLOWED")]
        public string? IsSalesPriceAdjustmentAllowed { get; set; }

        [JsonPropertyName("ISSALESPRICEINCLUDINGCHARGES")]
        public string? IsSalesPriceIncludingCharges { get; set; }

        [JsonPropertyName("ISSCALEPRODUCT")]
        public string? IsScaleProduct { get; set; }

        [JsonPropertyName("ISSHIPALONEENABLED")]
        public string? IsShipAloneEnabled { get; set; }

        [JsonPropertyName("ISUNITCOSTPRODUCTVARIANTSPECIFIC")]
        public string? IsUnitCostProductVariantSpecific { get; set; }

        [JsonPropertyName("ISVARIANTSHELFLABELSPRINTINGENABLED")]
        public string? IsVariantShelfLabelsPrintingEnabled { get; set; }

        [JsonPropertyName("ISZEROPRICEPOSREGISTRATIONALLOWED")]
        public string? IsZeroPricePosRegistrationAllowed { get; set; }

        [JsonPropertyName("KEYINPRICEREQUIREMENTSATPOSREGISTER")]
        public string? KeyInPriceRequirementsAtPosRegister { get; set; }

        [JsonPropertyName("KEYINQUANTITYREQUIREMENTSATPOSREGISTER")]
        public string? KeyInQuantityRequirementsAtPosRegister { get; set; }

        [JsonPropertyName("MARGINABCCODE")]
        public string? MarginAbcCode { get; set; }

        [JsonPropertyName("MAXIMUMPICKQUANTITY")]
        public decimal? MaximumPickQuantity { get; set; }

        [JsonPropertyName("MUSTKEYINCOMMENTATPOSREGISTER")]
        public string? MustKeyInCommentAtPosRegister { get; set; }

        [JsonPropertyName("NECESSARYPRODUCTIONWORKINGTIMESCHEDULINGPROPERTYID")]
        public string? NecessaryProductionWorkingTimeSchedulingPropertyId { get; set; }

        [JsonPropertyName("NETPRODUCTWEIGHT")]
        public decimal? NetProductWeight { get; set; }

        [JsonPropertyName("PACKINGDUTYQUANTITY")]
        public decimal? PackingDutyQuantity { get; set; }

        [JsonPropertyName("POSREGISTRATIONACTIVATIONDATE")]
        public DateTimeOffset? PosRegistrationActivationDate { get; set; }

        [JsonPropertyName("POSREGISTRATIONBLOCKEDDATE")]
        public DateTimeOffset? PosRegistrationBlockedDate { get; set; }

        [JsonPropertyName("POSREGISTRATIONPLANNEDBLOCKEDDATE")]
        public DateTimeOffset? PosRegistrationPlannedBlockedDate { get; set; }

        [JsonPropertyName("POTENCYBASEATTIBUTETARGETVALUE")]
        public decimal? PotencyBaseAttributeTargetValue { get; set; }

        [JsonPropertyName("POTENCYBASEATTRIBUTEVALUEENTRYEVENT")]
        public string? PotencyBaseAttributeValueEntryEvent { get; set; }

        [JsonPropertyName("PRODUCTTYPE")]
        public string? ProductType { get; set; }

        [JsonPropertyName("PRODUCTIONCONSUMPTIONDENSITYCONVERSIONFACTOR")]
        public decimal? ProductionConsumptionDensityConversionFactor { get; set; }

        [JsonPropertyName("PRODUCTIONCONSUMPTIONDEPTHCONVERSIONFACTOR")]
        public decimal? ProductionConsumptionDepthConversionFactor { get; set; }

        [JsonPropertyName("PRODUCTIONCONSUMPTIONHEIGHTCONVERSIONFACTOR")]
        public decimal? ProductionConsumptionHeightConversionFactor { get; set; }

        [JsonPropertyName("PRODUCTIONCONSUMPTIONWIDTHCONVERSIONFACTOR")]
        public decimal? ProductionConsumptionWidthConversionFactor { get; set; }

        [JsonPropertyName("PRODUCTVOLUME")]
        public decimal? ProductVolume { get; set; }

        [JsonPropertyName("PURCHASECHARGESQUANTITY")]
        public decimal? PurchaseChargesQuantity { get; set; }

        [JsonPropertyName("PURCHASEOVERDELIVERYPERCENTAGE")]
        public decimal? PurchaseOverDeliveryPercentage { get; set; }

        [JsonPropertyName("PURCHASEPRICE")]
        public decimal? PurchasePrice { get; set; }

        [JsonPropertyName("PURCHASEPRICEDATE")]
        public DateTimeOffset? PurchasePriceDate { get; set; }

        [JsonPropertyName("PURCHASEPRICINGPRECISION")]
        public int? PurchasePricingPrecision { get; set; }

        [JsonPropertyName("PURCHASEUNDERDELIVERYPERCENTAGE")]
        public decimal? PurchaseUnderDeliveryPercentage { get; set; }

        [JsonPropertyName("RAWMATERIALPICKINGPRINCIPLE")]
        public string? RawMaterialPickingPrinciple { get; set; }

        [JsonPropertyName("SALESCHARGESQUANTITY")]
        public decimal? SalesChargesQuantity { get; set; }

        [JsonPropertyName("SALESOVERDELIVERYPERCENTAGE")]
        public decimal? SalesOverDeliveryPercentage { get; set; }

        [JsonPropertyName("SALESPRICE")]
        public decimal? SalesPrice { get; set; }

        [JsonPropertyName("SALESPRICECALCULATIONCHARGESPERCENTAGE")]
        public decimal? SalesPriceCalculationChargesPercentage { get; set; }

        [JsonPropertyName("SALESPRICECALCULATIONCONTRIBUTIONRATIO")]
        public decimal? SalesPriceCalculationContributionRatio { get; set; }

        [JsonPropertyName("SALESPRICECALCULATIONMODEL")]
        public string? SalesPriceCalculationModel { get; set; }

        [JsonPropertyName("SALESPRICEDATE")]
        public DateTimeOffset? SalesPriceDate { get; set; }

        [JsonPropertyName("SALESPRICINGPRECISION")]
        public int? SalesPricingPrecision { get; set; }

        [JsonPropertyName("SALESUNDERDELIVERYPERCENTAGE")]
        public decimal? SalesUnderDeliveryPercentage { get; set; }

        [JsonPropertyName("SALESUNITSYMBOL")]
        public string? SalesUnitSymbol { get; set; }

        [JsonPropertyName("SCALEINDICATOR")]
        public string? ScaleIndicator { get; set; }

        [JsonPropertyName("SELLSTARTDATE")]
        public DateTimeOffset? SellStartDate { get; set; }

        [JsonPropertyName("SHELFADVICEPERIODDAYS")]
        public int? ShelfAdvicePeriodDays { get; set; }

        [JsonPropertyName("SHELFLIFEPERIODDAYS")]
        public int? ShelfLifePeriodDays { get; set; }

        [JsonPropertyName("SHIPSTARTDATE")]
        public DateTimeOffset? ShipStartDate { get; set; }

        [JsonPropertyName("TAREPRODUCTWEIGHT")]
        public decimal? TareProductWeight { get; set; }

        [JsonPropertyName("TRANSFERORDEROVERDELIVERYPERCENTAGE")]
        public decimal? TransferOrderOverDeliveryPercentage { get; set; }

        [JsonPropertyName("TRANSFERORDERUNDERDELIVERYPERCENTAGE")]
        public decimal? TransferOrderUnderDeliveryPercentage { get; set; }

        [JsonPropertyName("UNITCOST")]
        public decimal? UnitCost { get; set; }

        [JsonPropertyName("UNITCOSTDATE")]
        public DateTimeOffset? UnitCostDate { get; set; }

        [JsonPropertyName("UNITCOSTQUANTITY")]
        public decimal? UnitCostQuantity { get; set; }

        [JsonPropertyName("VARIABLESCRAPPERCENTAGE")]
        public decimal? VariableScrapPercentage { get; set; }

        [JsonPropertyName("WAREHOUSEMOBILEDEVICEDESCRIPTIONLINE1")]
        public string? WarehouseMobileDeviceDescriptionLine1 { get; set; }

        [JsonPropertyName("WAREHOUSEMOBILEDEVICEDESCRIPTIONLINE2")]
        public string? WarehouseMobileDeviceDescriptionLine2 { get; set; }

        [JsonPropertyName("WILLINVENTORYISSUEAUTOMATICALLYREPORTASFINISHED")]
        public string? WillInventoryIssueAutomaticallyReportAsFinished { get; set; }

        [JsonPropertyName("WILLINVENTORYRECEIPTIGNOREFLUSHINGPRINCIPLE")]
        public string? WillInventoryReceiptIgnoreFlushingPrinciple { get; set; }

        [JsonPropertyName("WILLPICKINGWORKBENCHAPPLYBOXINGLOGIC")]
        public string? WillPickingWorkbenchApplyBoxingLogic { get; set; }

        [JsonPropertyName("WILLTOTALPURCHASEDISCOUNTCALCULATIONINCLUDEPRODUCT")]
        public string? WillTotalPurchaseDiscountCalculationIncludeProduct { get; set; }

        [JsonPropertyName("WILLTOTALSALESDISCOUNTCALCULATIONINCLUDEPRODUCT")]
        public string? WillTotalSalesDiscountCalculationIncludeProduct { get; set; }

        [JsonPropertyName("WILLWORKCENTERPICKINGALLOWNEGATIVEINVENTORY")]
        public string? WillWorkCenterPickingAllowNegativeInventory { get; set; }

        [JsonPropertyName("YIELDPERCENTAGE")]
        public decimal? YieldPercentage { get; set; }

        [JsonPropertyName("ISUNITCOSTAUTOMATICALLYUPDATED")]
        public string? IsUnitCostAutomaticallyUpdated { get; set; }

        [JsonPropertyName("PURCHASEUNITSYMBOL")]
        public string? PurchaseUnitSymbol { get; set; }

        [JsonPropertyName("PURCHASEPRICEQUANTITY")]
        public decimal? PurchasePriceQuantity { get; set; }

        [JsonPropertyName("ISUNITCOSTINCLUDINGCHARGES")]
        public string? IsUnitCostIncludingCharges { get; set; }

        [JsonPropertyName("FIXEDCOSTCHARGES")]
        public decimal? FixedCostCharges { get; set; }

        [JsonPropertyName("MINIMUMCATCHWEIGHTQUANTITY")]
        public decimal? MinimumCatchWeightQuantity { get; set; }

        [JsonPropertyName("MAXIMUMCATCHWEIGHTQUANTITY")]
        public decimal? MaximumCatchWeightQuantity { get; set; }

        [JsonPropertyName("ALTERNATIVEITEMNUMBER")]
        public string? AlternativeItemNumber { get; set; }

        [JsonPropertyName("BOMUNITSYMBOL")]
        public string? BomUnitSymbol { get; set; }

        [JsonPropertyName("CATCHWEIGHTUNITSYMBOL")]
        public string? CatchWeightUnitSymbol { get; set; }

        [JsonPropertyName("COMPARISONPRICEBASEUNITSYMBOL")]
        public string? ComparisonPriceBaseUnitSymbol { get; set; }

        [JsonPropertyName("PRIMARYVENDORACCOUNTNUMBER")]
        public string? PrimaryVendorAccountNumber { get; set; }

        [JsonPropertyName("ISCATCHWEIGHTPRODUCT")]
        public string? IsCatchWeightProduct { get; set; }

        [JsonPropertyName("PRODUCTDIMENSIONGROUPNAME")]
        public string? ProductDimensionGroupName { get; set; }

        [JsonPropertyName("STORAGEDIMENSIONGROUPNAME")]
        public string? StorageDimensionGroupName { get; set; }

        [JsonPropertyName("TRACKINGDIMENSIONGROUPNAME")]
        public string? TrackingDimensionGroupName { get; set; }
    }

    public class FoODataResponse<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; set; } = new();

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }
}
