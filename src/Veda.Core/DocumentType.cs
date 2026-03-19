namespace Veda.Core;

public enum DocumentType
{
    BillInvoice,    // 账单/发票 -> 小颗粒 (256 token)
    Specification,  // 规范/PDS  -> 大颗粒 (1024 token)
    Report,         // 报告      -> 中颗粒 (512 token)
    Other           // 通用      -> 中颗粒 (512 token)
}
