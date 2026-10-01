#nullable disable
using System.Collections.Generic;
using System.Linq;

namespace Amanatsu.AiExtension;

// The maker's face and body sliders in the maker's own menus, tabs and order, with their Japanese labels,
// the API value each one drives, and what raising and lowering it does.
// Mapping: read from the maker UI (each slider moved once and the changed value recorded).
// Directions: shape sliders from the game's shape data (lib/chara/list/customshape.unity3d); rotations,
// positions of detailed values and paints checked with captures. Unchecked directions say 「向きは未確認」.
internal static class ShapeGuide
{
    // api: "shapes.face" / "shapes.body" (creator/shapes) or "params" (creator/params name, with selectors).
    sealed record Item(string Label, string Api, string Name, string Up, string Down, object Select = null);
    sealed record Tab(string Menu, string Name, string Note, Item[] Items);

    static Item F(string label, string name, string up, string down) => new(label, "shapes.face", name, up, down);
    static Item B(string label, string name, string up, string down) => new(label, "shapes.body", name, up, down);
    static Item Pm(string label, string name, string up, string down, object select = null) => new(label, "params", name, up, down, select);

    static Item[] Highlight(int index) => new[]
    {
        Pm("ハイライトの上下位置", "highlightY", "ハイライトが上下に動く（向きは未確認）", "逆向きに動く", new { highlight = index }),
        Pm("ハイライトの左右位置", "highlightX", "ハイライトがキャラの右（画面の左）へ動く（両目とも同じ向き）", "キャラの左（画面の右）へ動く", new { highlight = index }),
        Pm("ハイライトの横幅", "highlightWidth", "ハイライトが横に長くなる", "横に短くなる", new { highlight = index }),
        Pm("ハイライトの縦幅", "highlightHeight", "ハイライトが縦に長くなる", "縦に短くなる", new { highlight = index }),
        Pm("ハイライトの傾き", "highlightTilt", "ハイライトが傾く（向きは未確認）", "逆向きに傾く", new { highlight = index }),
    };

    static Item[] Paint(string prefix, int n) => new[]
    {
        Pm("左右補正", $"{prefix}{n}X", "ペイントが画面の右（キャラの左）へ動く", "画面の左（キャラの右）へ動く"),
        Pm("上下補正", $"{prefix}{n}Y", "ペイントが上へ動く", "下へ動く"),
        Pm("回転", $"{prefix}{n}Rotation", "ペイントが回転する（向きは未確認）", "逆向きに回転する"),
        Pm("サイズ補正", $"{prefix}{n}Size", "ペイントが大きくなる", "小さくなる"),
    };

    const string HairTransparencyUp = "前髪が透けて、目や眉が見えやすくなる";
    const string HairTransparencyDown = "前髪が透けなくなる";

    static readonly Tab[] Tabs =
    {
        new("顔", "輪郭", null, new[]
        {
            F("頭全体の横幅", "FaceBaseW", "顔全体の幅が広くなる", "顔全体の幅が狭くなる"),
            F("顔全体の上下", "FaceY", "顔全体（輪郭ごと）が、髪と首に対して上に移る", "顔全体（輪郭ごと）が、髪と首に対して下に移る"),
            F("顔全体のサイズ", "FaceSize", "顔が大きくなる（0.5 以下は元の大きさのまま変わらない）", "顔が小さくなる"),
            F("顔の上部前後", "FaceUpZ", "顔の上半分（額と眉のあたり）が前に出る", "顔の上半分が奥に下がる"),
            F("顔の上部上下", "FaceUpY", "顔の上半分が縦に長くなり、鼻筋も長くなる", "顔の上半分が縦に短くなる"),
            F("顔の上部サイズ", "FaceUpSize", "顔の上半分が大きくなる", "顔の上半分が小さくなる"),
            F("顔の下部前後", "FaceLowZ", "顔の下半分（口と顎）が前に出る", "顔の下半分が奥に下がる"),
            F("顔の下部横幅", "FaceLowW", "顔の下半分の幅が広くなる", "顔の下半分の幅が狭くなる"),
            F("頭部サイズ", "HeadSize", "頭（髪の土台）が元の大きさになる", "頭がわずかに小さくなる"),
        }),
        new("顔", "輪郭：頬", null, new[]
        {
            F("頬骨の幅", "CheekBoneW", "頬骨が外側に張る", "頬骨が内側に寄る"),
            F("頬骨の前後", "CheekBoneZ", "頬骨が前に出る", "頬骨が奥に下がる"),
            F("頬の幅", "CheekW", "頬が外側に張る（ふっくらした顔になる）", "頬が内側に寄る（ほっそりした顔になる）"),
            F("頬の前後", "CheekZ", "頬が前に出る", "頬が奥に下がる"),
            F("頬の上下", "CheekY", "頬の位置が上がる", "頬の位置が下がる"),
        }),
        new("顔", "輪郭：顎", null, new[]
        {
            F("顎の下部上下", "ChinLowY", "顎の下のラインが上がる（顎が短くなる）", "顎の下のラインが下がる（顎が長くなる）"),
            F("顎の下部奥行", "ChinLowZ", "エラが外側かつ下へ張り出す（顎が広く角ばる）", "エラが内側かつ上へ引っ込む（顎がほっそりする）"),
            F("顎の上下", "ChinY", "顎の位置が下がり、顔の下半分が長くなる", "顎の位置が上がり、顔の下半分が短く丸くなる"),
            F("顎の幅", "ChinW", "顎の幅が広くなる", "顎の幅が狭くなる"),
            F("顎の前後", "ChinZ", "顎が前に出る", "顎が奥に下がる"),
            F("顎の先上下", "ChinTipY", "顎先が下がる（長く尖る）", "顎先が上がる（短くなる）"),
            F("顎の先前後", "ChinTipZ", "顎先が前に出る", "顎先が奥に下がる"),
            F("顎の先幅", "ChinTipW", "顎先の幅が広くなる（丸みを帯びる）", "顎先の幅が狭くなる（尖る）"),
        }),
        new("顔", "輪郭：特殊表現", null, new[]
        {
            Pm("特殊表現の強さ", "faceDetailPower", "顔の陰影表現が濃くなる", "顔の陰影表現が薄くなる"),
        }),
        new("顔", "輪郭：ほくろ", "ほくろの種類と色は moleId / moleColor", new[]
        {
            Pm("左右補正", "moleX", "ほくろが画面の右（キャラの左）へ動く", "画面の左（キャラの右）へ動く"),
            Pm("上下補正", "moleY", "ほくろが上へ動く", "下へ動く"),
            Pm("サイズ補正", "moleSize", "ほくろが大きくなる", "小さくなる"),
        }),
        new("顔", "眉", null, new[]
        {
            Pm("髪の透過度", "hairTransparency", HairTransparencyUp, HairTransparencyDown),
            F("眉の上下", "EyebrowY", "眉の位置が上がる", "眉の位置が下がる"),
            F("眉の横位置", "EyebrowX", "眉が外側へ離れる", "眉が内側へ寄る"),
            F("眉の角度", "EyebrowRotZ", "眉尻が下がり、眉頭が上がる（困り眉になる）", "眉尻が上がり、眉頭が下がる（吊り眉になる）"),
            F("眉の内側形状", "EyebrowInForm", "眉の内側半分が上がる（眉頭が上がる）", "眉の内側半分が下がる（眉頭が下がる）"),
            F("眉の外側形状", "EyebrowOutForm", "眉尻の先が上に反る", "眉尻の先が下に曲がる"),
            Pm("眉の横幅", "eyebrowWidth", "眉が横に長くなる", "眉が横に短くなる"),
            Pm("眉の縦幅", "eyebrowHeight", "眉が太くなる", "眉が細くなる"),
        }),
        new("顔", "目", null, new[]
        {
            Pm("髪の透過度", "hairTransparency", HairTransparencyUp, HairTransparencyDown),
            F("目の上下", "EyeY", "目の位置が上がる", "目の位置が下がる"),
            F("目の横位置", "EyeX", "目が外側へ離れる", "目が内側へ寄る"),
            F("目の前後", "EyeZ", "目が前に出る（彫りが浅くなる）", "目が奥に下がる（彫りが深くなる）"),
            F("目の角度", "EyeTilt", "目尻が下がる（垂れ目になる）", "目尻が上がる（吊り目になる）"),
            F("目の縦幅", "EyeH", "目が縦に大きくなる（丸く大きな目になる）", "目が縦に小さくなる（細い目になる）"),
            F("目の横幅", "EyeW", "目の横幅が広くなる", "目の横幅が狭くなる"),
        }),
        new("顔", "目：まつ毛", null, new[]
        {
            F("上まつ毛の形状１", "EyelidsUpForm1", "上まぶたの目頭側が下がる（目頭側が閉じ気味になる）", "上まぶたの目頭側が上がる（目頭側が開く）"),
            F("上まつ毛の形状２", "EyelidsUpForm2", "上まぶたの中央が下がる（上辺が平らになり、眠たげな目になる）", "上まぶたの中央が上がる（上辺が丸く、ぱっちり開く）"),
            F("上まつ毛の形状３", "EyelidsUpForm3", "上まぶたの目尻側が上がる（目尻側が開く）", "上まぶたの目尻側が下がる（目尻側が重くなる）"),
            F("下まつ毛の形状１", "EyelidsLowForm1", "下まぶたの目頭側の点が、まぶたに沿って目の中央へ寄る", "その点が目頭側へ戻る"),
            F("下まつ毛の形状２", "EyelidsLowForm2", "下まぶたの中央が上がる（目が細くなる）", "下まぶたの中央が下がる（目が縦に大きくなる）"),
            F("下まつ毛の形状３", "EyelidsLowForm3", "下まぶたの目尻側が上がる", "下まぶたの目尻側が下がる"),
        }),
        new("顔", "目：まぶた", null, new[]
        {
            Pm("まぶたの横の位置", "eyelidX", "二重線が目頭側へ動く", "二重線が目尻側へ動く"),
            Pm("まぶたの縦の位置", "eyelidY", "二重線が上へ動く", "二重線が下へ動く"),
            Pm("まぶたの回転", "eyelidRotation", "二重線が傾く（向きは未確認）", "逆向きに傾く"),
            Pm("まぶたの大きさ", "eyelidSize", "二重線が大きくなる", "二重線が小さくなる"),
        }),
        new("顔", "目：瞳のプリセット", "「目：瞳」「目：瞳孔」と同じ値", new[]
        {
            Pm("瞳の左右位置", "irisX", "黒目が鼻側へ寄る", "黒目が目尻側へ寄る"),
            Pm("瞳の上下位置", "irisY", "黒目が上へ動く", "黒目が下へ動く"),
            Pm("瞳の幅", "irisWidth", "黒目が横に大きくなる", "黒目が横に小さくなる"),
            Pm("瞳の高さ", "irisHeight", "黒目が縦に大きくなる", "黒目が縦に小さくなる"),
            Pm("瞳孔の幅", "pupilWidth", "瞳孔が横に大きくなる", "瞳孔が横に小さくなる"),
            Pm("瞳孔の高さ", "pupilHeight", "瞳孔が縦に大きくなる", "瞳孔が縦に小さくなる"),
        }),
        new("顔", "目：瞳", null, new[]
        {
            Pm("瞳の左右位置", "irisX", "黒目が鼻側へ寄る", "黒目が目尻側へ寄る"),
            Pm("瞳の上下位置", "irisY", "黒目が上へ動く", "黒目が下へ動く"),
            Pm("瞳の幅", "irisWidth", "黒目が横に大きくなる", "黒目が横に小さくなる"),
            Pm("瞳の高さ", "irisHeight", "黒目が縦に大きくなる", "黒目が縦に小さくなる"),
        }),
        new("顔", "目：瞳孔", null, new[]
        {
            Pm("瞳孔の幅", "pupilWidth", "瞳孔が横に大きくなる", "瞳孔が横に小さくなる"),
            Pm("瞳孔の高さ", "pupilHeight", "瞳孔が縦に大きくなる", "瞳孔が縦に小さくなる"),
        }),
        new("顔", "目：瞳グラデ", null, new[]
        {
            Pm("目のグラデ位置", "eyeGradPosition", "瞳のグラデーションが下へ下がる（瞳の上側が元の色のまま残る）", "グラデーションが上へ上がる"),
            Pm("目のグラデサイズ", "eyeGradSize", "グラデーションが瞳の広い範囲にかかる", "グラデーションが狭い範囲にかかる"),
        }),
        new("顔", "目：ハイライト01", "params の highlight 0", Highlight(0)),
        new("顔", "目：ハイライト02", "params の highlight 1", Highlight(1)),
        new("顔", "目：ハイライト03", "params の highlight 2", Highlight(2)),
        new("顔", "目：白目", "スライダーなし（色は whiteBaseColor / whiteSubColor / whiteSub2Color）", new Item[0]),
        new("顔", "鼻", null, new[]
        {
            Pm("ハイライト強度", "noseGloss", "鼻のハイライトが強くなる", "鼻のハイライトが弱くなる"),
            F("鼻先の高さ", "NoseTipH", "鼻先が前に突き出る", "鼻先が低くなる"),
            F("鼻の上下", "NoseY", "鼻の位置が上がる", "鼻の位置が下がる"),
            F("鼻筋の高さ", "NoseBridgeH", "鼻筋が前に出る（鼻筋が高くなる）", "鼻筋が奥に下がる（鼻筋が低くなる）"),
        }),
        new("顔", "口", null, new[]
        {
            F("口の上下", "MouthY", "口の位置が上がる", "口の位置が下がる"),
            F("口の横幅", "MouthW", "口の幅が広くなる", "口の幅が狭くなる"),
            F("口の前後", "MouthZ", "口が前に出る", "口が奥に下がる"),
            F("口の形状上", "MouthUpForm", "上唇が前に出る（上唇が厚くなる）", "上唇が奥に下がる（上唇が薄くなる）"),
            F("口の形状下", "MouthLowForm", "下唇が前に出る（下唇が厚くなる）", "下唇が奥に下がる（下唇が薄くなる）"),
            F("口の形状口角角度", "MouthCornerFormRot", "口角が上向きに反る（笑った口の形になる）", "口角が下向きに反る（への字の口になる）"),
            F("口の形状口角上下", "MouthCornerFormY", "口角の位置が上がる（微笑んだ口になる）", "口角の位置が下がる"),
        }),
        new("顔", "耳", null, new[]
        {
            F("耳のサイズ", "EarSize", "耳が大きくなる", "耳が小さくなる（0.5 以下は元の大きさのまま変わらない）"),
            F("耳の角度Y軸", "EarRotY", "耳が縦の軸まわりに回る（向きは未確認。耳はたいてい髪に隠れている）", "逆向きに回る"),
            F("耳の角度Z軸", "EarRotZ", "耳が傾く（向きは未確認。耳はたいてい髪に隠れている）", "逆向きに傾く"),
            F("耳の上部形状", "EarUpForm", "耳の上部が外側かつ上へ伸び、小さく尖る（エルフ耳になる）", "耳の上部が丸いまま保たれる"),
            F("耳の下部形状", "EarLowForm", "耳たぶが下がる（耳たぶが長くなる）", "耳たぶが上がる"),
        }),
        new("顔", "化粧：チーク", null, new[]
        {
            Pm("チークの横の位置", "cheekX", "チークが外側（耳の方）へ動く", "チークが内側（鼻の方）へ動く"),
            Pm("チークの縦の位置", "cheekY", "チークが上へ動く", "チークが下へ動く"),
            Pm("チークの回転", "cheekRotation", "チークが回転する（向きは未確認）", "逆向きに回転する"),
            Pm("チークの大きさ", "cheekSize", "チークが大きくなる", "チークが小さくなる"),
        }),
        new("顔", "化粧：アイシャドウ", "スライダーなし（種類と色は creator/makeup）", new Item[0]),
        new("顔", "化粧：リップ", "スライダーなし（種類と色は creator/makeup）", new Item[0]),
        new("顔", "化粧：ペイント01", "種類と色は facePaint1Id / facePaint1Color", Paint("facePaint", 1)),
        new("顔", "化粧：ペイント02", "種類と色は facePaint2Id / facePaint2Color", Paint("facePaint", 2)),
        new("顔", "化粧：ペイント03", "種類と色は facePaint3Id / facePaint3Color", Paint("facePaint", 3)),
        new("顔", "変形まとめ（顔）", "ほかのタブの顔の形状スライダーをまとめたタブ（固有のスライダーはない）", new Item[0]),

        new("体", "肌と頭身", "肌の色や種類は creator/skin", new[]
        {
            B("身長", "Height", "背が高くなる", "背が低くなる（頭の大きさは保たれる）"),
            B("頭のサイズ", "HeadSize", "頭が大きくなる", "頭が小さくなる"),
            Pm("肉感の強さ", "bodyDetailPower", "体の陰影表現（肉感）が強くなる", "体の陰影表現が弱くなる"),
            Pm("ツヤの強さ", "skinShinePower", "肌のツヤが強くなる", "肌のツヤが弱くなる"),
        }),
        new("体", "胸", null, new[]
        {
            B("胸のサイズ", "BustSize", "胸が大きくなる", "胸が小さくなる"),
            B("胸の上下位置", "BustY", "胸の位置が上がる", "胸の位置が下がる"),
            B("胸の左右開き", "BustRotX", "胸が外向きになる（左右に離れて見える）", "胸が正面向きになり、中央に寄る"),
            B("胸の左右位置", "BustX", "胸の間隔が広がる", "胸の間隔が狭まる"),
            B("胸の上下角度", "BustRotY", "胸が上向きになる", "胸が下向きになる"),
            B("胸の尖り", "BustSharp", "胸の形が尖る（円錐形に近づく）", "胸の形が丸くなる"),
            B("胸の形状", "BustForm1", "胸の上側のカーブが平らになる", "胸の上側のカーブがふくらむ"),
            B("胸の根本の角度", "BustForm2", "胸の下側のカーブが持ち上がり、前に張り出す", "胸の下側のカーブが垂れて平らになる"),
            Pm("胸の柔らかさ", "bustSoftness", "胸がよく揺れる", "胸に張りが出て揺れにくくなる"),
            Pm("胸の重さ", "bustWeight", "胸が重く、下がり気味になる", "胸が軽く、上がり気味になる"),
        }),
        new("体", "胸：乳首", "乳首の種類と色は nipId / nipColor", new[]
        {
            B("乳輪の膨らみ", "AreolaBulge", "乳輪がふくらむ（ぷっくりする）", "乳輪が平らになる"),
            B("乳首の太さ", "NipWeight", "乳首が大きくなる", "乳首が小さくなる"),
            B("乳首立ち", "NipStand", "乳首が前に突き出る", "乳首が平らになる"),
            Pm("乳輪の大きさ", "areolaSize", "乳輪が大きくなる", "乳輪が小さくなる"),
        }),
        new("体", "上半身", null, new[]
        {
            B("首のサイズ", "NeckSize", "首が太くなる", "首が細くなる"),
            B("首元の上下位置", "NeckY", "首が長くなる", "首が短くなる"),
            B("胴体肩周りの幅", "BodyShoulderW", "胸の上部と肩まわりの幅が広くなる", "胸の上部と肩まわりの幅が狭くなる"),
            B("胴体肩周りの奥", "BodyShoulderZ", "胸の上部の厚み（前後）が増す", "胸の上部の厚みが減る"),
            B("胴体上の幅", "BodyUpW", "胸郭の幅が広くなる", "胸郭の幅が狭くなる"),
            B("胴体上の奥", "BodyUpZ", "胸郭の厚み（前後）が増す", "胸郭の厚みが減る"),
            B("胴体下の幅", "BodyLowW", "胴の下部の幅が広くなる", "胴の下部の幅が狭くなる"),
            B("胴体下の奥", "BodyLowZ", "胴の下部の厚み（前後）が増す", "胴の下部の厚みが減る"),
        }),
        new("体", "上半身：腕", null, new[]
        {
            B("肩の幅", "ShoulderW", "肩幅が広くなる", "肩幅が狭くなる"),
            B("肩のサイズ", "ShoulderZ", "肩に厚みが出る（丸みを帯びる）", "肩が薄くなる"),
            B("上腕のサイズ", "ArmUpSize", "二の腕が太くなる", "二の腕が細くなる"),
            B("ヒジのサイズ", "ElbowSize", "肘が太くなる", "肘が細くなる"),
            B("前腕のサイズ", "ArmFront", "前腕が太くなる", "前腕が細くなる"),
            B("手首のサイズ", "WristSize", "手首が太くなる", "手首が細くなる"),
            B("手と指の太さ", "HandW", "手が大きくなる", "手が小さくなる"),
        }),
        new("体", "下半身", null, new[]
        {
            B("ウエストの上下位置", "WaistY", "くびれの位置が上がる", "くびれの位置が下がる"),
            B("腹部のサイズ", "Belly", "お腹が丸く前に出る", "お腹が平らになる"),
            B("腰上の幅", "WaistUpW", "ウエストの幅が広くなる", "ウエストの幅が狭くなる（くびれが強くなる）"),
            B("腰上の奥", "WaistUpZ", "ウエストの厚み（前後）が増す", "ウエストの厚みが減る"),
            B("腰下の幅", "WaistLowW", "腰（骨盤）の幅が広くなる", "腰の幅が狭くなる"),
            B("腰下の奥", "WaistLowZ", "骨盤まわりの厚み（前後）が増す", "骨盤まわりの厚みが減る"),
            B("尻のサイズ", "HipSize", "お尻が大きくなる", "お尻が小さくなる"),
            B("尻の上下角度", "HipRotX", "お尻の位置が上がる（上向きのお尻になる）", "お尻の位置が下がる"),
            B("尻の縦幅", "HipSizeY", "お尻が縦に大きくなる", "お尻が縦に小さくなる"),
        }),
        new("体", "下半身：脚", null, new[]
        {
            B("太もも上のサイズ", "ThighUpSize", "太ももの上部が太くなる", "太ももの上部が細くなる"),
            B("太もも下のサイズ", "ThighLowSize", "太ももの下部（膝の上）が太くなる", "太ももの下部が細くなる"),
            B("ヒザのサイズ", "KneeSize", "膝が大きくなる", "膝が小さくなる"),
            B("ふくらはぎのサイズ", "Calf", "ふくらはぎが太くなる", "ふくらはぎが細くなる"),
            B("足首のサイズ", "AnkleSize", "足首が太くなる", "足首が細くなる"),
        }),
        new("体", "柔らかさ", null, new[]
        {
            Pm("胸の柔らかさ", "bustSoftness", "胸がよく揺れる", "胸に張りが出て揺れにくくなる"),
            Pm("胸の重さ", "bustWeight", "胸が重く、下がり気味になる", "胸が軽く、上がり気味になる"),
            Pm("上腕の柔らかさ", "upperArmSoftness", "二の腕がよく揺れる", "二の腕が揺れにくくなる"),
            Pm("腹部の柔らかさ", "bellySoftness", "お腹がよく揺れる", "お腹が揺れにくくなる"),
            Pm("腰の柔らかさ", "waistSoftness", "腰がよく揺れる", "腰が揺れにくくなる"),
            Pm("尻の柔らかさ", "hipSoftness", "お尻がよく揺れる", "お尻が揺れにくくなる"),
            Pm("太ももの柔らかさ", "thighSoftness", "太ももがよく揺れる", "太ももが揺れにくくなる"),
        }),
        new("体", "陰毛", "スライダーなし（種類と色は underhairId / underhairColor）", new Item[0]),
        new("体", "日焼け01", "スライダーなし（種類は creator/skin の sunburnUpId、色は sunburnUpColor）", new Item[0]),
        new("体", "日焼け02", "スライダーなし（種類は creator/skin の sunburnDownId、色は sunburnDownColor）", new Item[0]),
        new("体", "化粧：手のネイル", "種類と色は nailId / nailColor1〜3", new[]
        {
            Pm("ネイルのツヤの強さ", "nailGloss", "爪のツヤが強くなる", "爪のツヤが弱くなる", new { foot = false }),
        }),
        new("体", "化粧：足のネイル", "種類と色は nailId / nailColor1〜3（foot: true）", new[]
        {
            Pm("ネイルのツヤの強さ", "nailGloss", "足の爪のツヤが強くなる", "足の爪のツヤが弱くなる", new { foot = true }),
        }),
        new("体", "化粧：ペイント01", "種類と色は bodyPaint1Id / bodyPaint1Color", Paint("bodyPaint", 1)),
        new("体", "化粧：ペイント02", "種類と色は bodyPaint2Id / bodyPaint2Color", Paint("bodyPaint", 2)),
        new("体", "化粧：ペイント03", "種類と色は bodyPaint3Id / bodyPaint3Color", Paint("bodyPaint", 3)),
        new("体", "簡易操作", "複数の形状スライダーをまとめて動かすスライダー。API では下の各値を個別に設定する", new[]
        {
            new Item("胴体横幅", "shapes.body", "NeckSize, BodyShoulderW, BodyUpW, BodyLowW", "首・胴体肩周り・胴体上・胴体下の幅がまとめて広がる", "まとめて狭まる"),
            new Item("胴体厚み", "shapes.body", "BodyShoulderZ, BodyUpZ, BodyLowZ, Belly", "胴体の厚みとお腹がまとめて増す", "まとめて減る"),
            new Item("下腹部横幅", "shapes.body", "WaistUpW, WaistLowW", "腰上と腰下の幅がまとめて広がる", "まとめて狭まる"),
            new Item("下腹部厚み", "shapes.body", "WaistUpZ, WaistLowZ, HipSize", "腰の厚みとお尻の大きさがまとめて増す", "まとめて減る"),
            new Item("腕の太さ", "shapes.body", "ShoulderW, ShoulderZ, ArmUpSize, ElbowSize, ArmFront, WristSize, HandW", "肩から手までがまとめて太くなる", "まとめて細くなる"),
            new Item("脚の太さ", "shapes.body", "ThighUpSize, ThighLowSize, KneeSize, Calf, AnkleSize", "太ももから足首までがまとめて太くなる", "まとめて細くなる"),
        }),
        new("体", "変形まとめ（身体）", "ほかのタブの体のスライダーをまとめたタブ（固有のスライダーはない）", new Item[0]),
    };

    // Values that the maker does not show on any tab.
    static readonly Item[] Hidden =
    {
        F("（エディターに表示なし）", "NoseBase", "小鼻（鼻の付け根）の位置が上がる", "小鼻の位置が下がる"),
    };

    // Raise/lower notes for creator/params, taken from the tabs above.
    internal static readonly Dictionary<string, (string up, string down)> ParamNotes =
        Tabs.SelectMany(t => t.Items).Where(i => i.Api == "params")
            .GroupBy(i => i.Name).ToDictionary(g => g.Key, g => (g.First().Up, g.First().Down));

    internal static object All() => new
    {
        note = "ゲーム内エディターのメニュー・タブ・並び順どおり。値は 0〜1（形状は 0.5 が標準）。0〜1 の外の値も API はそのまま書き込みます。ゲームに反映されるかどうかは環境によります。" +
               "api が shapes.face / shapes.body なら creator/shapes、params なら creator/params（select は同時に渡す選択子）で設定する。",
        tabs = Tabs.Select(t => new
        {
            menu = t.Menu, tab = t.Name, note = t.Note,
            sliders = t.Items.Select(i => new { label = i.Label, api = i.Api, name = i.Name, select = i.Select, increase = i.Up, decrease = i.Down }).ToArray()
        }).ToArray(),
        hidden = Hidden.Select(i => new { api = i.Api, name = i.Name, increase = i.Up, decrease = i.Down }).ToArray()
    };
}
