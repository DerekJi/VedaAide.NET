namespace Veda.Core;

public enum DocumentType
{
    BillInvoice,    // Invoice/receipt      -> small chunks (256 tokens), Document Intelligence prebuilt-invoice
    Specification,  // Spec/PDS             -> large chunks (1024 tokens)
    Report,         // Report               -> medium chunks (512 tokens)
    PersonalNote,   // Personal note/memo   -> small chunks (256 tokens)
    RichMedia,      // Rich media           -> medium chunks (512 tokens), extracted via Vision model (GPT-4o-mini)
    Identity,       // Passport/ID/driver's license -> small chunks (256 tokens), Document Intelligence prebuilt-idDocument
    Certificate,    // Certificate/award    -> small chunks (256 tokens), skips PdfPig, uses Azure DI / Vision
    Other           // Generic              -> medium chunks (512 tokens)
}
