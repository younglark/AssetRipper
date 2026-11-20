using AssetRipper.Export.PrimaryContent;
using System.Globalization;

namespace AssetRipper.CLI;

/// <summary>Represents non-finite numbers as named JSON strings, without rewriting payload text.</summary>
internal sealed class AssetJsonWalker(TextWriter writer) : DefaultJsonWalker(writer)
{
	public override void VisitPrimitive<T>(T value)
	{
		if (value is float single && !float.IsFinite(single))
		{
			Writer.Write('"');
			Writer.Write(single.ToString(CultureInfo.InvariantCulture));
			Writer.Write('"');
		}
		else if (value is double number && !double.IsFinite(number))
		{
			Writer.Write('"');
			Writer.Write(number.ToString(CultureInfo.InvariantCulture));
			Writer.Write('"');
		}
		else
		{
			base.VisitPrimitive(value);
		}
	}
}
