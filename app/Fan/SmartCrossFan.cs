using System;
using System.Linq;
using GHelper;
using GHelper.Fan;
using GHelper.Gpu;
using GHelper.USB;
using GHelper.Mode;

namespace GHelper.Fan
{
    public static class SmartCrossFan
    {
        // Ayarlar (AppConfig ile override edilebilir)
        private static int TempThreshold => AppConfig.Get("cross_temp", 75);
        private static int Hysteresis => AppConfig.Get("cross_hyst", 5);

        // State cache
        private static bool _cpuHigh = false;
        private static bool _gpuHigh = false;

        private static int _lastAppliedCpuPercent = -1;
        private static int _lastAppliedGpuPercent = -1;

        private static bool _overrideActiveCpu = false;
        private static bool _overrideActiveGpu = false;

        // Public entry point
        public static void Apply(bool force = false)
        {
            // Feature flag: AppConfig key "cross_fan" (1=on)
            if (!force && !AppConfig.Is("cross_fan")) return;

            float? cpuTempNullable = HardwareControl.GetCPUTemp();
            float? gpuTempNullable = HardwareControl.GetGPUTemp();

            if (cpuTempNullable is null || gpuTempNullable is null) return;

            float cpuTemp = cpuTempNullable.Value;
            float gpuTemp = gpuTempNullable.Value;

            // Hysteresis logic: rising/falling thresholds per sensor
            bool cpuWasHigh = _cpuHigh;
            bool gpuWasHigh = _gpuHigh;

            if (cpuTemp >= TempThreshold) _cpuHigh = true;
            else if (cpuTemp <= TempThreshold - Hysteresis) _cpuHigh = false;
            // else keep previous

            if (gpuTemp >= TempThreshold) _gpuHigh = true;
            else if (gpuTemp <= TempThreshold - Hysteresis) _gpuHigh = false;
            // else keep previous

            // Read configured curves (fallback to ACPI default if missing)
            byte[] cpuCurve = AppConfig.GetFanConfig(AsusFan.CPU);
            byte[] gpuCurve = AppConfig.GetFanConfig(AsusFan.GPU);

            if (AsusACPI.IsInvalidCurve(cpuCurve))
                cpuCurve = Program.acpi.GetFanCurve(AsusFan.CPU, Modes.GetCurrentBase());
            if (AsusACPI.IsInvalidCurve(gpuCurve))
                gpuCurve = Program.acpi.GetFanCurve(AsusFan.GPU, Modes.GetCurrentBase());

            // Evaluate requested percentages at current temps
            int cpuRequested = EvaluateCurveAtTemp(cpuCurve, cpuTemp); // 0..100
            int gpuRequested = EvaluateCurveAtTemp(gpuCurve, gpuTemp); // 0..100

            // Decide state
            if (_cpuHigh && !_gpuHigh)
            {
                // CPU sıcak, GPU soğuk -> GPU'yu CPU'nun istediği hıza zorla
                ApplyOverride(AsusFan.GPU, gpuRequested: cpuRequested, cpuRequested: cpuRequested);
            }
            else if (_gpuHigh && !_cpuHigh)
            {
                // GPU sıcak, CPU soğuk -> CPU'yu GPU'nun istediği hıza zorla
                ApplyOverride(AsusFan.CPU, cpuRequested: gpuRequested, gpuRequested: gpuRequested);
            }
            else
            {
                // İkisi de yüksek veya ikisi de düşük -> restore defaults
                if ((_cpuHigh && _gpuHigh) || (!_cpuHigh && !_gpuHigh))
                {
                    RestoreIfNeeded();
                }
            }

            // Debug log (sık olmaması için sadece durum değiştiğinde)
            if (cpuWasHigh != _cpuHigh || gpuWasHigh != _gpuHigh)
            {
                Logger.WriteLine($"SmartCrossFan state: CPUHigh={_cpuHigh} GPUHigh={_gpuHigh} (T={TempThreshold}, Hyst={Hysteresis}) CPUtemp={cpuTemp} GPUtemp={gpuTemp}");
            }
        }

        private static void ApplyOverride(AsusFan targetFan, int cpuRequested, int gpuRequested)
        {
            // Eğer sadece GPU override edilecekse targetFan == GPU ve desiredPercent = cpuRequested
            // Eğer sadece CPU override edilecekse targetFan == CPU and desiredPercent = gpuRequested

            if (targetFan == AsusFan.GPU)
            {
                int desired = cpuRequested;
                if (_lastAppliedGpuPercent == desired && _overrideActiveGpu) return;

                byte[] flat = CreateFlatCurve(desired);
                int res = Program.acpi.SetFanCurve(AsusFan.GPU, flat);

                if (res == 1)
                {
                    Logger.WriteLine($"SmartCrossFan: Override GPU -> {desired}%");
                    _lastAppliedGpuPercent = desired;
                    _overrideActiveGpu = true;
                }
            }
            else // CPU
            {
                int desired = gpuRequested;
                if (_lastAppliedCpuPercent == desired && _overrideActiveCpu) return;

                byte[] flat = CreateFlatCurve(desired);
                int res = Program.acpi.SetFanCurve(AsusFan.CPU, flat);

                if (res == 1)
                {
                    Logger.WriteLine($"SmartCrossFan: Override CPU -> {desired}%");
                    _lastAppliedCpuPercent = desired;
                    _overrideActiveCpu = true;
                }
            }
        }

        private static void RestoreIfNeeded()
        {
            // Restore CPU
            if (_overrideActiveCpu)
            {
                byte[] cpuCurve = AppConfig.GetFanConfig(AsusFan.CPU);
                if (AsusACPI.IsInvalidCurve(cpuCurve))
                    cpuCurve = Program.acpi.GetFanCurve(AsusFan.CPU, Modes.GetCurrentBase());

                int res = Program.acpi.SetFanCurve(AsusFan.CPU, cpuCurve);
                if (res == 1)
                {
                    Logger.WriteLine("SmartCrossFan: Restored CPU curve");
                    _overrideActiveCpu = false;
                    _lastAppliedCpuPercent = -1;
                }
            }

            // Restore GPU
            if (_overrideActiveGpu)
            {
                byte[] gpuCurve = AppConfig.GetFanConfig(AsusFan.GPU);
                if (AsusACPI.IsInvalidCurve(gpuCurve))
                    gpuCurve = Program.acpi.GetFanCurve(AsusFan.GPU, Modes.GetCurrentBase());

                int res = Program.acpi.SetFanCurve(AsusFan.GPU, gpuCurve);
                if (res == 1)
                {
                    Logger.WriteLine("SmartCrossFan: Restored GPU curve");
                    _overrideActiveGpu = false;
                    _lastAppliedGpuPercent = -1;
                }
            }
        }

        // Create a flat 16-byte ASUS curve: Xs are monotonic, Ys are all 'percent'
        private static byte[] CreateFlatCurve(int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            byte[] curve = new byte[16];

            // Temperatue knots (monotonic). Use common defaults: 30,40,50,60,70,80,90,100
            byte[] xs = new byte[] { 30, 40, 50, 60, 70, 80, 90, 100 };
            for (int i = 0; i < 8; i++) curve[i] = xs[i];

            for (int i = 0; i < 8; i++) curve[8 + i] = (byte)percent;

            return curve;
        }

        // Evaluate curve (16 bytes) at provided temp (C) -> percentage 0..100
        private static int EvaluateCurveAtTemp(byte[] curve, float temp)
        {
            if (curve is null || curve.Length != 16) return 0;

            // Extract X and Y
            float[] xs = new float[8];
            float[] ys = new float[8];
            for (int i = 0; i < 8; i++)
            {
                xs[i] = curve[i];
                ys[i] = curve[i + 8];
            }

            // If temp <= first X
            if (temp <= xs[0]) return (int)Math.Round(ys[0]);

            // If temp >= last X
            if (temp >= xs[7]) return (int)Math.Round(ys[7]);

            // Find segment and linearly interpolate
            for (int i = 0; i < 7; i++)
            {
                if (temp >= xs[i] && temp <= xs[i + 1])
                {
                    float t = (temp - xs[i]) / (xs[i + 1] - xs[i]);
                    float v = ys[i] + t * (ys[i + 1] - ys[i]);
                    return (int)Math.Round(Math.Max(0, Math.Min(100, v)));
                }
            }

            return 0;
        }
    }
}