namespace Wakeel.Crypto.Tests;

public class RecoveryCodeTests
{
    [Fact]
    public void A_generated_code_has_twenty_data_characters_and_one_check_character()
    {
        var code = RecoveryCode.Generate();

        Assert.Equal(RecoveryCode.TotalCharacters, code.Raw.Length);
        Assert.Equal(RecoveryCode.KeyMaterialSize, code.KeyMaterial.Length);
        Assert.Equal(0, code.KeyMaterial[0] & 0xF0);
    }

    [Fact]
    public void The_printed_form_is_five_groups_with_the_check_character_in_the_last_one()
    {
        var code = RecoveryCode.Generate();
        var groups = code.Display.Split('-');

        Assert.Equal(5, groups.Length);
        Assert.All(groups[..4], group => Assert.Equal(4, group.Length));
        Assert.Equal(5, groups[4].Length);
        Assert.Equal(code.Raw, code.Display.Replace("-", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void A_generated_code_parses_back_to_the_same_secret()
    {
        var code = RecoveryCode.Generate();

        Assert.True(RecoveryCode.TryParse(code.Display, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(code.Raw, parsed!.Raw);
        Assert.Equal(code.KeyMaterial, parsed.KeyMaterial);
    }

    [Fact]
    public void Normalization_folds_the_letters_people_confuse()
    {
        var code = RecoveryCode.Generate();
        var typed = code.Display
            .ToLowerInvariant()
            .Replace('0', 'o')
            .Replace('1', 'l')
            .Replace("-", " ", StringComparison.Ordinal);

        Assert.True(RecoveryCode.TryParse(typed, out var parsed));
        Assert.Equal(code.Raw, parsed!.Raw);
    }

    [Theory]
    [InlineData("o", "0")]
    [InlineData("i", "1")]
    [InlineData("L", "1")]
    [InlineData("a-b c", "ABC")]
    [InlineData("", "")]
    public void Normalize_applies_the_documented_substitutions(string input, string expected) =>
        Assert.Equal(expected, RecoveryCode.Normalize(input));

    [Fact]
    public void A_wrong_check_character_is_refused()
    {
        var code = RecoveryCode.Generate();
        var broken = code.Raw[..^1] + (code.Raw[^1] == '7' ? '8' : '7');

        Assert.False(RecoveryCode.TryParse(broken, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void A_single_changed_data_character_is_refused_by_the_check_character()
    {
        var refused = 0;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var code = RecoveryCode.Generate();
            var characters = code.Raw.ToCharArray();
            characters[3] = characters[3] == 'A' ? 'B' : 'A';
            if (!RecoveryCode.TryParse(new string(characters), out _))
            {
                refused++;
            }
        }

        Assert.Equal(30, refused);
    }

    [Fact]
    public void A_code_of_the_wrong_length_is_refused()
    {
        Assert.False(RecoveryCode.TryParse("ABCD-EFGH", out _));
        Assert.False(RecoveryCode.TryParse(null, out _));
    }

    [Fact]
    public void An_excluded_letter_never_appears_in_the_data_part()
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var raw = RecoveryCode.Generate().Raw[..RecoveryCode.DataCharacters];
            Assert.DoesNotContain('I', raw);
            Assert.DoesNotContain('L', raw);
            Assert.DoesNotContain('O', raw);
            Assert.DoesNotContain('U', raw);
        }
    }

    [Fact]
    public void Parse_reports_a_corrupt_code_with_the_matching_reason()
    {
        var error = Assert.Throws<CryptoException>(() => RecoveryCode.Parse("not a code"));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
    }

    [Fact]
    public void The_same_secret_always_renders_the_same_code()
    {
        var material = new byte[RecoveryCode.KeyMaterialSize];
        material[^1] = 1;

        var first = RecoveryCode.FromKeyMaterial(material);
        var again = RecoveryCode.FromKeyMaterial(material);

        Assert.Equal(first.Raw, again.Raw);
        Assert.Equal("0000-0000-0000-0000-00011", first.Display);
    }
}
