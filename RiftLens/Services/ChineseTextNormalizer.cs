using System.Text;

namespace RiftLens.Services;

/// <summary>
/// Deterministic Traditional-to-Simplified normalization for LoL client text.
/// Official regional names are additionally resolved through zh_TW Data Dragon;
/// this character layer focuses on fixed UI verbs and common client wording.
/// </summary>
public static class ChineseTextNormalizer
{
    private static readonly (string Traditional, string Simplified)[] Phrases =
    {
        ("發出信號", "发出信号"),
        ("告知所有人敵人消失", "告知所有人敌人消失"),
        ("告知敵人有此處的視野", "告知敌人有此处的视野"),
        ("表示警惕此處", "表示警惕此处"),
        ("要求隊友盡速支援", "要求队友尽速支援"),
        ("請求撤退", "请求撤退"),
        ("已使用閃現", "已使用闪现"),
        ("已使用點燃", "已使用点燃"),
        ("已購買", "已购买"),
        ("已擊殺", "已击杀"),
        ("控域守衛", "控制守卫"),
        ("極域守衛", "控制守卫"),
        ("极域守卫", "控制守卫"),
        ("虛空幼蟲", "虚空巢虫"),
        ("生化科技飛龍", "炼金科技亚龙"),
        ("治癒", "治疗"),
        ("打野任務完成", "打野任务完成"),
        ("單/雙排積分", "单/双排积分")
    };

    public static string ToSimplifiedCharacters(string? value)
    {
        var normalized = value ?? string.Empty;
        foreach (var (traditional, simplified) in Phrases)
        {
            normalized = normalized.Replace(traditional, simplified, StringComparison.Ordinal);
        }

        var result = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            result.Append(MapCharacter(character));
        }
        return result.ToString();
    }

    private static char MapCharacter(char character) => character switch
    {
        '發' => '发', '號' => '号', '敵' => '敌', '處' => '处', '視' => '视',
        '隊' => '队', '盡' => '尽', '請' => '请', '險' => '险', '購' => '购',
        '買' => '买', '擊' => '击', '殺' => '杀', '飛' => '飞', '龍' => '龙',
        '虛' => '虚', '蟲' => '虫', '癒' => '愈', '換' => '换', '單' => '单',
        '雙' => '双', '積' => '积', '負' => '负', '勝' => '胜', '閃' => '闪',
        '現' => '现', '點' => '点', '烏' => '乌', '爾' => '尔', '凱' => '凯',
        '亞' => '亚', '歷' => '历', '馬' => '马', '夢' => '梦', '奪' => '夺',
        '氣' => '气', '殤' => '殇', '護' => '护', '衛' => '卫', '體' => '体',
        '這' => '这', '裡' => '里', '還' => '还', '沒' => '没', '為' => '为',
        '與' => '与', '後' => '后', '開' => '开', '關' => '关', '時' => '时',
        '間' => '间', '級' => '级', '學' => '学', '術' => '术', '經' => '经',
        '驗' => '验', '資' => '资', '訊' => '讯', '標' => '标', '記' => '记',
        '顯' => '显', '傷' => '伤', '輕' => '轻', '劍' => '剑', '靈' => '灵',
        '萬' => '万', '變' => '变', '亙' => '亘', '務' => '务', '療' => '疗',
        '區' => '区', '達' => '达', '詩' => '诗', '個' => '个', '對' => '对',
        '無' => '无', '擋' => '挡', '賽' => '赛', '終' => '终', '結' => '结',
        '邊' => '边', '蘭' => '兰', '鳥' => '鸟', '費' => '费', '誌' => '志',
        '徑' => '径',
        _ => character
    };
}
