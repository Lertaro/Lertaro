namespace Lertaro.Core.Services.LocalSend;

/// <summary>
/// 1:1 implementation of official LocalSend random alias generator algorithm.
/// Generates localized friendly device names like "帅气的土豆" or "Cool Potato".
/// </summary>
public static class LocalSendAliasGenerator
{
    private static readonly string[] ZhAdjectives =
    [
        "迷人", "美丽", "巨大", "明亮", "干净", "聪明", "帅气", "可爱", "狡猾", "坚定",
        "有活力", "高效", "极好", "快速", "不错", "新鲜", "好", "华丽", "伟大", "英俊",
        "炽热", "善良", "诚实", "神秘", "整洁", "开心", "耐心", "漂亮", "强大", "富有",
        "秘密", "聪明", "稳固", "特别", "战略性", "强大", "整洁", "智慧"
    ];

    private static readonly string[] ZhFruits =
    [
        "苹果", "鳄梨", "香蕉", "黑莓", "蓝莓", "西兰花", "胡萝卜", "樱桃", "椰子", "葡萄",
        "柠檬", "莴苣", "芒果", "甜瓜", "蘑菇", "洋葱", "橙子", "木瓜", "桃子", "梨",
        "菠萝", "土豆", "南瓜", "覆盆子", "草莓", "番茄"
    ];

    private static readonly string[] EnAdjectives =
    [
        "Adorable", "Beautiful", "Big", "Bright", "Clean", "Clever", "Cool", "Cute",
        "Cunning", "Determined", "Energetic", "Efficient", "Fantastic", "Fast", "Fine",
        "Fresh", "Good", "Gorgeous", "Great", "Handsome", "Hot", "Kind", "Lovely",
        "Mystic", "Neat", "Nice", "Patient", "Pretty", "Powerful", "Rich", "Secret",
        "Smart", "Solid", "Special", "Strategic", "Strong", "Tidy", "Wise"
    ];

    private static readonly string[] EnFruits =
    [
        "Apple", "Avocado", "Banana", "Blackberry", "Blueberry", "Broccoli", "Carrot",
        "Cherry", "Coconut", "Grape", "Lemon", "Lettuce", "Mango", "Melon", "Mushroom",
        "Onion", "Orange", "Papaya", "Peach", "Pear", "Pineapple", "Potato", "Pumpkin",
        "Raspberry", "Strawberry", "Tomato"
    ];

    private static readonly string[] EsAdjectives =
    [
        "Adorable", "Bonita", "Grande", "Brillante", "Limpia", "Lista", "Fresca", "Linda",
        "Astuta", "Decidida", "Energica", "Eficiente", "Fantastica", "Rapida", "Buena",
        "Hermosa", "Genial", "Elegante", "Caliente", "Amable", "Mistica", "Ordenada",
        "Paciente", "Poderosa", "Rica", "Secreta", "Inteligente", "Solida", "Especial",
        "Estratega", "Fuerte", "Sabia"
    ];

    private static readonly string[] EsFruits =
    [
        "Manzana", "Aguacate", "Platano", "Mora", "Arandano", "Brocoli", "Zanahoria",
        "Cereza", "Coco", "Uva", "Limon", "Lechuga", "Mango", "Melon", "Champiñon",
        "Cebolla", "Naranja", "Papaya", "Melocoton", "Pera", "Piña", "Patata",
        "Calabaza", "Frambuesa", "Fresa", "Tomate"
    ];

    private static readonly string[] JaAdjectives =
    [
        "チャーミングな", "美しい", "巨大な", "明るい", "清潔な", "賢い", "かっこいい", "可愛い",
        "狡猾な", "断固とした", "エネルギッシュな", "効率的な", "すばらしい", "速い", "素晴らしい",
        "新鮮な", "良い", "華やかな", "偉大な", "ハンサムな", "情熱的な", "親切な", "正直な",
        "神秘的な", "きちんとした", "幸せな", "忍耐強い", "綺麗な", "強力な", "裕福な", "秘密の",
        "スマートな", "堅实な", "特別な", "戦略的な", "強い", "静かな", "賢明な"
    ];

    private static readonly string[] JaFruits =
    [
        "リンゴ", "アボカド", "バナナ", "ブラックベリー", "ブルーベリー", "ブロッコリー", "ニンジン",
        "サクランボ", "ココナッツ", "ブドウ", "レモン", "レタス", "マンゴー", "メロン", "マッシュルーム",
        "タマネギ", "オレンジ", "パパイヤ", "モモ", "ナシ", "パイナップル", "ジャガイモ",
        "カボチャ", "木イチゴ", "イチゴ", "トマト"
    ];

    private static readonly string[] KoAdjectives =
    [
        "매력적인", "아름다운", "거대한", "밝은", "깨끗한", "영리한", "멋진", "귀여운",
        "교활한", "단호한", "활기찬", "효율적인", "환상적인", "빠른", "훌륭한", "신선한",
        "좋은", "화려한", "위대한", "잘생긴", "뜨거운", "친절한", "정직한", "신비로운",
        "단정한", "행복한", "인내심있는", "예쁜", "강력한", "부유한", "비밀의", "똑똑한",
        "견고한", "특별한", "전략적인", "강한", "깔끔한", "현명한"
    ];

    private static readonly string[] KoFruits =
    [
        "사과", "아보카도", "바나나", "블랙베리", "블루베리", "브로콜리", "당근", "체리",
        "코코넛", "포도", "레몬", "상추", "망고", "멜론", "버섯", "양파", "오렌지",
        "파파야", "복숭아", "배", "파인애플", "감자", "단호박", "산딸기", "딸기", "토마토"
    ];

    public static string GenerateRandomAlias(string? cultureName = null)
    {
        var lang = cultureName ?? System.Globalization.CultureInfo.CurrentUICulture.Name;

        if (lang.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ||
            lang.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase) ||
            (lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase) &&
             !lang.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase) &&
             !lang.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) &&
             !lang.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)))
        {
            var adj = ZhAdjectives[Random.Shared.Next(ZhAdjectives.Length)];
            var fruit = ZhFruits[Random.Shared.Next(ZhFruits.Length)];
            return $"{adj}的{fruit}";
        }

        if (lang.StartsWith("es", StringComparison.OrdinalIgnoreCase))
        {
            var adj = EsAdjectives[Random.Shared.Next(EsAdjectives.Length)];
            var fruit = EsFruits[Random.Shared.Next(EsFruits.Length)];
            return $"{fruit} {adj}";
        }

        if (lang.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            var adj = JaAdjectives[Random.Shared.Next(JaAdjectives.Length)];
            var fruit = JaFruits[Random.Shared.Next(JaFruits.Length)];
            return $"{adj}{fruit}";
        }

        if (lang.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
        {
            var adj = KoAdjectives[Random.Shared.Next(KoAdjectives.Length)];
            var fruit = KoFruits[Random.Shared.Next(KoFruits.Length)];
            return $"{adj} {fruit}";
        }

        var enAdj = EnAdjectives[Random.Shared.Next(EnAdjectives.Length)];
        var enFruit = EnFruits[Random.Shared.Next(EnFruits.Length)];
        return $"{enAdj} {enFruit}";
    }
}
