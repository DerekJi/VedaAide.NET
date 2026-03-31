namespace Veda.Core;

public enum DocumentType
{
    BillInvoice,    // 账单/发票     -> 小颗粒 (256 token)，Document Intelligence prebuilt-invoice
    Specification,  // 规范/PDS     -> 大颗粒 (1024 token)
    Report,         // 报告         -> 中颗粒 (512 token)
    PersonalNote,   // 个人备注/笔记 -> 小颗粒 (256 token)
    RichMedia,      // 富媒体        -> 中颗粒 (512 token)，Vision 模型提取（GPT-4o-mini）
    Identity,       // 护照/身份证/驾照 -> 小颗粒 (256 token)，Document Intelligence prebuilt-idDocument
    Other           // 通用         -> 中颗粒 (512 token)
}
