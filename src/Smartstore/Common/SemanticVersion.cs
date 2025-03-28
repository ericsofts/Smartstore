﻿using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Smartstore
{
    /// <summary>
    /// A semantic version implementation.
    /// Conforms with v2.0.0 of http://semver.org
    /// </summary>
    [JsonConverter(typeof(SemanticVersionJsonConverter))]
    public class SemanticVersion : IComparable<SemanticVersion>, IComparable
    {
        private static readonly Regex ParseEx =
            new(@"^(?<major>\d+)" +
                @"(?>\.(?<minor>\d+))?" +
                @"(?>\.(?<patch>\d+))?" +
                @"(?>\-(?<pre>[0-9A-Za-z\-\.]+))?" +
                @"(?>\+(?<build>[0-9A-Za-z\-\.]+))?$",
                RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
                TimeSpan.FromSeconds(0.5));

        /// <summary>
        /// Constructs a new instance of the <see cref="SemanticVersion" /> class.
        /// </summary>
        /// <param name="major">The major version.</param>
        /// <param name="minor">The minor version.</param>
        /// <param name="patch">The patch version.</param>
        /// <param name="prerelease">The prerelease version (e.g. "alpha").</param>
        /// <param name="build">The build metadata (e.g. "nightly.232").</param>
        public SemanticVersion(int major, int minor = 0, int patch = 0, string prerelease = "", string build = "")
        {
            Major = major;
            Minor = minor;
            Patch = patch;

            Prerelease = prerelease ?? string.Empty;
            Build = build ?? string.Empty;
        }

        /// <summary>
        /// Constructs a new instance of the <see cref="SemanticVersion"/> class from
        /// a <see cref="System.Version"/>.
        /// </summary>
        /// <param name="version">The <see cref="Version"/> that is used to initialize
        /// the Major, Minor, Patch and Build.</param>
        /// <returns>A <see cref="SemanticVersion"/> with the same Major and Minor version.
        /// The Patch version will be the fourth part of the version number. The
        /// build meta data will contain the third part of the version number if
        /// it is greater than zero.</returns>
        public SemanticVersion(Version version)
        {
            if (version == null)
                throw new ArgumentNullException(nameof(version));

            Major = version.Major;
            Minor = version.Minor;

            if (version.Revision >= 0)
                Patch = version.Revision;

            Prerelease = string.Empty;

            Build = version.Build > 0 ? version.Build.ToString(CultureInfo.InvariantCulture) : string.Empty;
        }

        /// <summary>
        /// Converts the string representation of a semantic version to its <see cref="SemanticVersion"/> equivalent.
        /// </summary>
        /// <param name="version">The version string.</param>
        /// <param name="strict">If set to <see langword="true"/> minor and patch version are required,
        /// otherwise they are optional.</param>
        /// <returns>The <see cref="SemanticVersion"/> object.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="version"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">The <paramref name="version"/> has an invalid format.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="version"/> is missing Minor or Patch versions and <paramref name="strict"/> is <see langword="true"/>.</exception>
        /// <exception cref="OverflowException">The Major, Minor, or Patch versions are larger than <code>int.MaxValue</code>.</exception>
        public static SemanticVersion Parse(string version, bool strict = false)
        {
            var match = ParseEx.Match(version);
            if (!match.Success)
                throw new ArgumentException($"Invalid version '{version}'.", nameof(version));

            var major = int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture);

            var minorMatch = match.Groups["minor"];
            int minor = 0;
            if (minorMatch.Success)
                minor = int.Parse(minorMatch.Value, CultureInfo.InvariantCulture);
            else if (strict)
                throw new InvalidOperationException("Invalid version (no minor version given in strict mode)");

            var patchMatch = match.Groups["patch"];
            int patch = 0;
            if (patchMatch.Success)
                patch = int.Parse(patchMatch.Value, CultureInfo.InvariantCulture);
            else if (strict)
                throw new InvalidOperationException("Invalid version (no patch version given in strict mode)");

            var prerelease = match.Groups["pre"].Value;
            var build = match.Groups["build"].Value;

            return new SemanticVersion(major, minor, patch, prerelease, build);
        }

        /// <summary>
        /// Converts the string representation of a semantic version to its <see cref="SemanticVersion"/>
        /// equivalent and returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="version">The version string.</param>
        /// <param name="semver">When the method returns, contains a <see cref="SemanticVersion"/> instance equivalent
        /// to the version string passed in, if the version string was valid, or <see langword="null"/> if the
        /// version string was not valid.</param>
        /// <param name="strict">If set to <see langword="true"/> minor and patch version are required,
        /// otherwise they are optional.</param>
        /// <returns><see langword="false"/> when a invalid version string is passed, otherwise <see langword="true"/>.</returns>
        public static bool TryParse(string version, out SemanticVersion semver, bool strict = false)
        {
            semver = null;
            if (version is null) return false;

            var match = ParseEx.Match(version);
            if (!match.Success) return false;

            if (!int.TryParse(match.Groups["major"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var major))
                return false;

            var minorMatch = match.Groups["minor"];
            int minor = 0;
            if (minorMatch.Success)
            {
                if (!int.TryParse(minorMatch.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out minor))
                    return false;
            }
            else if (strict) return false;

            var patchMatch = match.Groups["patch"];
            int patch = 0;
            if (patchMatch.Success)
            {
                if (!int.TryParse(patchMatch.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out patch))
                    return false;
            }
            else if (strict) return false;

            var prerelease = match.Groups["pre"].Value;
            var build = match.Groups["build"].Value;

            semver = new SemanticVersion(major, minor, patch, prerelease, build);
            return true;
        }

        /// <summary>
        /// Checks whether two semantic versions are equal.
        /// </summary>
        /// <param name="versionA">The first version to compare.</param>
        /// <param name="versionB">The second version to compare.</param>
        /// <returns><see langword="true"/> if the two values are equal, otherwise <see langword="false"/>.</returns>
        public static bool Equals(SemanticVersion versionA, SemanticVersion versionB)
        {
            if (ReferenceEquals(versionA, versionB)) return true;
            if (versionA is null || versionB is null) return false;
            return versionA.Equals(versionB);
        }

        /// <summary>
        /// Compares the specified versions.
        /// </summary>
        /// <param name="versionA">The first version to compare.</param>
        /// <param name="versionB">The second version to compare.</param>
        /// <returns>A signed number indicating the relative values of <paramref name="versionA"/> and <paramref name="versionB"/>.</returns>
        public static int Compare(SemanticVersion versionA, SemanticVersion versionB)
        {
            if (ReferenceEquals(versionA, versionB)) return 0;
            if (versionA is null) return -1;
            if (versionB is null) return 1;
            return versionA.CompareTo(versionB);
        }

        /// <summary>
        /// Make a copy of the current instance with changed properties.
        /// </summary>
        /// <param name="major">The value to replace the major version or <see langword="null"/> to leave it unchanged.</param>
        /// <param name="minor">The value to replace the minor version or <see langword="null"/> to leave it unchanged.</param>
        /// <param name="patch">The value to replace the patch version or <see langword="null"/> to leave it unchanged.</param>
        /// <param name="prerelease">The value to replace the prerelease version or <see langword="null"/> to leave it unchanged.</param>
        /// <param name="build">The value to replace the build metadata or <see langword="null"/> to leave it unchanged.</param>
        /// <returns>The new version object.</returns>
        /// <remarks>
        /// The change method is intended to be called using named argument syntax, passing only
        /// those fields to be changed.
        /// </remarks>
        /// <example>
        /// To change only the patch version:
        /// <code>version.Change(patch: 4)</code>
        /// </example>
        public SemanticVersion Change(int? major = null, int? minor = null, int? patch = null, string prerelease = null, string build = null)
        {
            return new SemanticVersion(
                major ?? Major,
                minor ?? Minor,
                patch ?? Patch,
                prerelease ?? Prerelease,
                build ?? Build);
        }

        /// <summary>
        /// Gets the major version.
        /// </summary>
        /// <value>
        /// The major version.
        /// </value>
        public int Major { get; }

        /// <summary>
        /// Gets the minor version.
        /// </summary>
        /// <value>
        /// The minor version.
        /// </value>
        public int Minor { get; }

        /// <summary>
        /// Gets the patch version.
        /// </summary>
        /// <value>
        /// The patch version.
        /// </value>
        public int Patch { get; }

        /// <summary>
        /// Gets the prerelease version.
        /// </summary>
        /// <value>
        /// The prerelease version. Empty string if this is a release version.
        /// </value>
        public string Prerelease { get; }

        /// <summary>
        /// Gets the build metadata.
        /// </summary>
        /// <value>
        /// The build metadata. Empty string if there is no build metadata.
        /// </value>
        public string Build { get; }

        /// <summary>
        /// Returns the <see cref="string" /> equivalent of this version.
        /// </summary>
        /// <returns>
        /// The <see cref="string" /> equivalent of this version.
        /// </returns>
        public override string ToString()
        {
            // Assume all separators ("..-+"), at most 2 extra chars
            var estimatedLength = 4 + Major.NumDigits() + Minor.NumDigits() + Patch.NumDigits()
                                  + Prerelease.Length + Build.Length;
            var version = new StringBuilder(estimatedLength);
            version.Append(Major);
            version.Append('.');
            version.Append(Minor);
            version.Append('.');
            version.Append(Patch);
            if (Prerelease.Length > 0)
            {
                version.Append('-');
                version.Append(Prerelease);
            }
            if (Build.Length > 0)
            {
                version.Append('+');
                version.Append(Build);
            }
            return version.ToString();
        }

        /// <summary>
        /// Compares the current instance with another object of the same type and returns an integer that indicates
        /// whether the current instance precedes, follows, or occurs in the same position in the sort order as the
        /// other object.
        /// </summary>
        /// <param name="obj">An object to compare with this instance.</param>
        /// <returns>
        /// A value that indicates the relative order of the objects being compared.
        /// The return value has these meanings:
        ///  Less than zero: This instance precedes <paramref name="obj" /> in the sort order.
        ///  Zero: This instance occurs in the same position in the sort order as <paramref name="obj" />.
        ///  Greater than zero: This instance follows <paramref name="obj" /> in the sort order.
        /// </returns>
        /// <exception cref="InvalidCastException">The <paramref name="obj"/> is not a <see cref="SemanticVersion"/>.</exception>
        public int CompareTo(object obj)
        {
            return CompareTo((SemanticVersion)obj);
        }

        /// <summary>
        /// Compares the current instance with another object of the same type and returns an integer that indicates
        /// whether the current instance precedes, follows, or occurs in the same position in the sort order as the
        /// other object.
        /// </summary>
        /// <param name="other">An object to compare with this instance.</param>
        /// <returns>
        /// A value that indicates the relative order of the objects being compared.
        /// The return value has these meanings:
        ///  Less than zero: This instance precedes <paramref name="other" /> in the sort order.
        ///  Zero: This instance occurs in the same position in the sort order as <paramref name="other" />.
        ///  Greater than zero: This instance follows <paramref name="other" /> in the sort order.
        /// </returns>
        public int CompareTo(SemanticVersion other)
        {
            var r = CompareByPrecedence(other);
            if (r != 0) return r;

#pragma warning disable CA1062 // Validate arguments of public methods
            // If other is null, CompareByPrecedence() returns 1
            return CompareComponent(Build, other.Build);
#pragma warning restore CA1062 // Validate arguments of public methods
        }

        /// <summary>
        /// Returns whether two semantic versions have the same precedence. Versions
        /// that differ only by build metadata have the same precedence.
        /// </summary>
        /// <param name="other">The semantic version to compare to.</param>
        /// <returns><see langword="true"/> if the version precedences are equal.</returns>
        public bool PrecedenceMatches(SemanticVersion other)
        {
            return CompareByPrecedence(other) == 0;
        }

        /// <summary>
        /// Compares two semantic versions by precedence as defined in the SemVer spec. Versions
        /// that differ only by build metadata have the same precedence.
        /// </summary>
        /// <param name="other">The semantic version.</param>
        /// <returns>
        /// A value that indicates the relative order of the objects being compared.
        /// The return value has these meanings:
        ///  Less than zero: This instance precedes <paramref name="other" /> in the sort order.
        ///  Zero: This instance occurs in the same position in the sort order as <paramref name="other" />.
        ///  Greater than zero: This instance follows <paramref name="other" /> in the sort order.
        /// </returns>
        public int CompareByPrecedence(SemanticVersion other)
        {
            if (other is null)
                return 1;

            var r = Major.CompareTo(other.Major);
            if (r != 0) return r;

            r = Minor.CompareTo(other.Minor);
            if (r != 0) return r;

            r = Patch.CompareTo(other.Patch);
            if (r != 0) return r;

            return CompareComponent(Prerelease, other.Prerelease, true);
        }

        private static int CompareComponent(string a, string b, bool nonemptyIsLower = false)
        {
            var aEmpty = string.IsNullOrEmpty(a);
            var bEmpty = string.IsNullOrEmpty(b);
            if (aEmpty && bEmpty)
                return 0;

            if (aEmpty)
                return nonemptyIsLower ? 1 : -1;
            if (bEmpty)
                return nonemptyIsLower ? -1 : 1;

            var aComps = a.Split('.');
            var bComps = b.Split('.');

            var minLen = Math.Min(aComps.Length, bComps.Length);
            for (int i = 0; i < minLen; i++)
            {
                var ac = aComps[i];
                var bc = bComps[i];
                var aIsNum = int.TryParse(ac, out var aNum);
                var bIsNum = int.TryParse(bc, out var bNum);
                int r;
                if (aIsNum && bIsNum)
                {
                    r = aNum.CompareTo(bNum);
                    if (r != 0) return r;
                }
                else
                {
                    if (aIsNum)
                        return -1;
                    if (bIsNum)
                        return 1;
                    r = string.CompareOrdinal(ac, bc);
                    if (r != 0)
                        return r;
                }
            }

            return aComps.Length.CompareTo(bComps.Length);
        }

        /// <summary>
        /// Determines whether the specified <see cref="object" /> is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="object" /> to compare with this instance.</param>
        /// <returns>
        ///   <see langword="true"/> if the specified <see cref="object" /> is equal to this instance, otherwise <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidCastException">The <paramref name="obj"/> is not a <see cref="SemanticVersion"/>.</exception>
        public override bool Equals(object obj)
        {
            if (obj is null)
                return false;

            if (ReferenceEquals(this, obj))
                return true;

            var other = (SemanticVersion)obj;

            return Major == other.Major
                && Minor == other.Minor
                && Patch == other.Patch
                && string.Equals(Prerelease, other.Prerelease, StringComparison.Ordinal)
                && string.Equals(Build, other.Build, StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table.
        /// </returns>
        public override int GetHashCode()
        {
            unchecked
            {
                // TODO verify this. Some versions start result = 17. Some use 37 instead of 31
                int result = Major.GetHashCode();
                result = result * 31 + Minor.GetHashCode();
                result = result * 31 + Patch.GetHashCode();
                result = result * 31 + Prerelease.GetHashCode();
                result = result * 31 + Build.GetHashCode();
                return result;
            }
        }

#pragma warning disable CA2225 // Operator overloads have named alternates
        /// <summary>
        /// Implicit conversion from <see cref="string"/> to <see cref="SemanticVersion"/>.
        /// </summary>
        /// <param name="version">The semantic version.</param>
        /// <returns>The <see cref="SemanticVersion"/> object.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="version"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">The version number has an invalid format.</exception>
        /// <exception cref="OverflowException">The Major, Minor, or Patch versions are larger than <code>int.MaxValue</code>.</exception>
        public static implicit operator SemanticVersion(string version)
#pragma warning restore CA2225 // Operator overloads have named alternates
        {
            return Parse(version);
        }

        /// <summary>
        /// Compares two semantic versions for equality.
        /// </summary>
        /// <param name="left">The left value.</param>
        /// <param name="right">The right value.</param>
        /// <returns>If left is equal to right <see langword="true"/>, otherwise <see langword="false"/>.</returns>
        public static bool operator ==(SemanticVersion left, SemanticVersion right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares two semantic versions for inequality.
        /// </summary>
        /// <param name="left">The left value.</param>
        /// <param name="right">The right value.</param>
        /// <returns>If left is not equal to right <see langword="true"/>, otherwise <see langword="false"/>.</returns>
        public static bool operator !=(SemanticVersion left, SemanticVersion right)
        {
            return !Equals(left, right);
        }

        /// <summary>
        /// Compares two semantic versions.
        /// </summary>
        /// <param name="left">The left value.</param>
        /// <param name="right">The right value.</param>
        /// <returns>If left is greater than right <see langword="true"/>, otherwise <see langword="false"/>.</returns>
        public static bool operator >(SemanticVersion left, SemanticVersion right)
        {
            return Compare(left, right) > 0;
        }

        /// <summary>
        /// Compares two semantic versions.
        /// </summary>
        /// <param name="left">The left value.</param>
        /// <param name="right">The right value.</param>
        /// <returns>If left is greater than or equal to right <see langword="true"/>, otherwise <see langword="false"/>.</returns>
        public static bool operator >=(SemanticVersion left, SemanticVersion right)
        {
            return Equals(left, right) || Compare(left, right) > 0;
        }

        /// <summary>
        /// Compares two semantic versions.
        /// </summary>
        /// <param name="left">The left value.</param>
        /// <param name="right">The right value.</param>
        /// <returns>If left is less than right <see langword="true"/>, otherwise <see langword="false"/>.</returns>
        public static bool operator <(SemanticVersion left, SemanticVersion right)
        {
            return Compare(left, right) < 0;
        }

        /// <summary>
        /// Compares two semantic versions.
        /// </summary>
        /// <param name="left">The left value.</param>
        /// <param name="right">The right value.</param>
        /// <returns>If left is less than or equal to right <see langword="true"/>, otherwise <see langword="false"/>.</returns>
        public static bool operator <=(SemanticVersion left, SemanticVersion right)
        {
            return Equals(left, right) || Compare(left, right) < 0;
        }
    }

    internal sealed class SemanticVersionJsonConverter : JsonConverter<SemanticVersion>
    {
        public override bool CanRead
            => true;

        public override bool CanWrite
            => true;

        public override SemanticVersion ReadJson(JsonReader reader, Type objectType, SemanticVersion existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            reader.Read();
            return reader.ReadAsString();
        }

        public override void WriteJson(JsonWriter writer, SemanticVersion value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value.ToString());
        }
    }
}