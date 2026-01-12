using FSMExpress.Common.Document;

namespace FSMExpress.PlayMaker;
public class FsmPlaymakerValue(IFsmPlaymakerValuePreviewer? value, string name, int indent) : FsmDocumentNodeFieldValue
{
    public override int DisplayIndent => indent;
    public override string DisplayType => value?.GetType().Name ?? "null";
    public override string DisplayString
    {
        get
        {
            if (value == null)
                return "null" + GetNameString(name);

            var baseStr = value.ToString();
            return FieldKind switch
            {
                FsmDocumentNodeDataFieldKind.Float => baseStr + "f" + GetNameString(name),
                FsmDocumentNodeDataFieldKind.String => "\"" + baseStr + "\"" + GetNameString(name),
                _ => baseStr + GetNameString(name)
            };
        }
    }
    public override FsmDocumentNodeDataFieldKind FieldKind => value?.FieldKind ?? FsmDocumentNodeDataFieldKind.Object;
}
