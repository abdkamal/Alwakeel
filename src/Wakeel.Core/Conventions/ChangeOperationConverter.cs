using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Wakeel.Core.Data;

namespace Wakeel.Core.Conventions;

/// <summary>Stores <see cref="ChangeOperation"/> as the single-letter code used by the change_log triggers ("I"/"U").</summary>
internal sealed class ChangeOperationConverter : ValueConverter<ChangeOperation, string>
{
    public ChangeOperationConverter()
        : base(
            v => v == ChangeOperation.Insert ? "I" : "U",
            v => v == "I" ? ChangeOperation.Insert : ChangeOperation.Update)
    {
    }
}
