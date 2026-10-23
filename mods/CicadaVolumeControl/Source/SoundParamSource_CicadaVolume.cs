using Verse;
using Verse.Sound;

namespace WhiteGiver.CicadaVolumeControl;

public sealed class SoundParamSource_CicadaVolume : SoundParamSource
{
    public override string Label => "CVC_Category".Translate();

    // ponytail: 原生 Constant 映射每帧读取设置；无需修改基础音量或重建正在播放的声音。
    public override float ValueFor(Sample samp) => CicadaVolumeMod.Settings.VolumePercent / 100f;
}
