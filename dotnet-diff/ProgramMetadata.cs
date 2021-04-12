using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DotnetDiff
{
    public class ProgramMetadata
    {
        private static readonly JsonSerializerOptions _serializerOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private static ProgramMetadata Default { get; } = new ProgramMetadata(new MetadataHolder());

        private readonly MetadataHolder _holder;

        private ProgramMetadata(MetadataHolder holder) => _holder = holder;

        public static ProgramMetadata Open(string path)
        {
            ProgramMetadata metadata;
            try
            {
                metadata = new(JsonSerializer.Deserialize<MetadataHolder>(File.ReadAllBytes(path), _serializerOptions) ?? new MetadataHolder());
            }
            catch (Exception exception) when (exception is FileNotFoundException or JsonException)
            {
                metadata = Default;
            }

            return metadata;
        }

        public bool JitUtilsSetUp { get => _holder.JitUtils; set => _holder.JitUtils = value; }

        public void AddSdk(FrameworkVersion version) => UpdateSdk(version);

        public void AddCrossgen2(FrameworkVersion version)
        {
            UpdateSdk(version);
            UpdateCrossgen2(version);
        }

        public void AddTarget(FrameworkVersion sdk, RuntimeIdentifier target)
        {
            UpdateSdk(sdk);
            UpdateTarget(sdk, target);
        }

        public void AddRuntimeAssemblies(FrameworkVersion sdk, RuntimeIdentifier target)
        {
            UpdateSdk(sdk);
            UpdateTarget(sdk, target);
            UpdateRuntimeAssemblies(sdk, target);
        }

        public void AddJit(FrameworkVersion sdk, RuntimeIdentifier target)
        {
            UpdateSdk(sdk);
            UpdateTarget(sdk, target);
            UpdateJit(sdk, target);
        }

        public bool SdkIsAvailable(FrameworkVersion sdk) => SdkIsDefined(sdk);

        public bool FullSdkIsAvailable(FrameworkVersion sdk)
        {
            if (!_holder.Sdks.TryGetValue(sdk.RawValue, out var sdkHolder) || !sdkHolder.Crossgen2)
            {
                return false;
            }

            return true;
        }

        public bool Crossgen2IsAvailable(FrameworkVersion sdk) => _holder.Sdks.TryGetValue(sdk.RawValue, out var sdkHolder) && sdkHolder.Crossgen2;

        public bool FullTargetIsAvailable(FrameworkVersion sdk, RuntimeIdentifier target)
        {
            if (!_holder.Sdks.TryGetValue(sdk.RawValue, out var sdkHolder))
            {
                return false;
            }

            if (!sdkHolder.Targets.TryGetValue(target.ToString(), out var targetHolder) ||
                !targetHolder.Jit ||
                !targetHolder.RuntimeAssemblies)
            {
                return false;
            }

            return true;
        }

        public bool TargetIsAvailable(FrameworkVersion sdk, RuntimeIdentifier target) => TargetIsDefined(sdk, target);

        public bool RuntimeAssembliesAreAvailable(FrameworkVersion sdk, RuntimeIdentifier target) => _holder.Sdks.TryGetValue(sdk.RawValue, out var sdkHolder) && sdkHolder.Targets.TryGetValue(target.ToString(), out var targetHolder) && targetHolder.RuntimeAssemblies;

        public bool JitIsAvailable(FrameworkVersion sdk, RuntimeIdentifier target) => _holder.Sdks.TryGetValue(sdk.RawValue, out var sdkHolder) && sdkHolder.Targets.TryGetValue(target.ToString(), out var targetHolder) && targetHolder.Jit;

        public IEnumerable<FrameworkVersion> EnumerateSdks() => _holder.Sdks.Select(x => new FrameworkVersion(x.Key));

        public IEnumerable<RuntimeIdentifier> EnumerateTargets(FrameworkVersion sdk) => GetSdk(sdk).Targets.Select(x => RuntimeIdentifier.Parse(x.Key));

        public void Save(string path) => File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(_holder, _serializerOptions));

        private void UpdateSdk(FrameworkVersion sdk)
        {
            if (!SdkIsDefined(sdk))
            {
                ReplaceSdk(sdk);
            }
        }

        private void UpdateCrossgen2(FrameworkVersion sdk) => ReplaceCrossgen2(sdk);

        private void UpdateTarget(FrameworkVersion sdk, RuntimeIdentifier target)
        {
            if (!TargetIsDefined(sdk, target))
            {
                ReplaceTarget(sdk, target);
            }
        }

        private void UpdateRuntimeAssemblies(FrameworkVersion sdk, RuntimeIdentifier target) => ReplaceRuntimeAssemblies(sdk, target);

        private void UpdateJit(FrameworkVersion sdk, RuntimeIdentifier target) => ReplaceJit(sdk, target);

        private bool SdkIsDefined(FrameworkVersion sdk) => _holder.Sdks.ContainsKey(sdk.RawValue);
        private bool TargetIsDefined(FrameworkVersion sdk, RuntimeIdentifier target) => _holder.Sdks.TryGetValue(sdk.RawValue, out var targets) && targets.Targets.ContainsKey(target.ToString());

        private void ReplaceSdk(FrameworkVersion sdk) => _holder.Sdks[sdk.RawValue] = new();
        private void ReplaceCrossgen2(FrameworkVersion sdk) => GetSdk(sdk).Crossgen2 = true;
        private void ReplaceTarget(FrameworkVersion sdk, RuntimeIdentifier target) => GetSdk(sdk).Targets[target.ToString()] = new();
        private void ReplaceRuntimeAssemblies(FrameworkVersion sdk, RuntimeIdentifier target) => GetTarget(sdk, target).RuntimeAssemblies = true;
        private void ReplaceJit(FrameworkVersion sdk, RuntimeIdentifier target) => GetTarget(sdk, target).Jit = true;

        private SdkHolder GetSdk(FrameworkVersion sdk) => _holder.Sdks[sdk.RawValue];
        private TargetHolder GetTarget(FrameworkVersion sdk, RuntimeIdentifier target) => GetSdk(sdk).Targets[target.ToString()];

        private class MetadataHolder
        {
            public bool JitUtils { get; set; }
            public Dictionary<string, SdkHolder> Sdks { get; set; } = new Dictionary<string, SdkHolder>();
        }

        private sealed record SdkHolder
        {
            public bool Crossgen2 { get; set; }
            public Dictionary<string, TargetHolder> Targets { get; set; } = new Dictionary<string, TargetHolder>();
        }

        private sealed record TargetHolder
        {
            public bool RuntimeAssemblies { get; set; }
            public bool Jit { get; set; }
        }
    }
}
