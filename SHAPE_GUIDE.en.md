# Shape guide (English)

English version of `GET /api/v1/creator/shape-guide`. It lists the character
maker's face and body sliders by menu and tab, in the maker's own order, with the
maker's Japanese label, an English gloss, the API value each slider drives, and what
raising (`+`) and lowering (`-`) it does.

- Values run 0 to 1; shape sliders are neutral at 0.5. Values outside 0 to 1 are
  outside the game's own slider range.
- `face:` and `body:` names are set with `POST /api/v1/creator/shapes`
  (`{"face":{"EyeW":0.6}}`). `param:` names are set with `POST /api/v1/creator/params`
  (`{"values":{"pupilWidth":0.6}}`), together with the selector shown in braces.
- "Left" and "right" are the character's own unless the text says "screen".
- "Direction not confirmed" marks directions that could not be checked in the game.

## Face (顔)

### 輪郭 (Outline)

- 頭全体の横幅 (head width) `face:FaceBaseW` — + the whole face becomes wider / - narrower
- 顔全体の上下 (face height position) `face:FaceY` — + the whole face, outline included, moves up against the hair and neck / - moves down
- 顔全体のサイズ (face size) `face:FaceSize` — + the face becomes larger (0.5 and below keep the base size) / - smaller
- 顔の上部前後 (upper face depth) `face:FaceUpZ` — + the upper face (forehead and brow area) comes forward / - sits back
- 顔の上部上下 (upper face height) `face:FaceUpY` — + the upper face becomes taller and the nose bridge longer / - shorter
- 顔の上部サイズ (upper face size) `face:FaceUpSize` — + the upper face becomes larger / - smaller
- 顔の下部前後 (lower face depth) `face:FaceLowZ` — + the lower face (mouth and jaw) comes forward / - sits back
- 顔の下部横幅 (lower face width) `face:FaceLowW` — + the lower face becomes wider / - narrower
- 頭部サイズ (head size) `face:HeadSize` — + the head (hair base) is at full size / - slightly smaller

### 輪郭：頬 (Outline: cheeks)

- 頬骨の幅 (cheekbone width) `face:CheekBoneW` — + the cheekbones move outward / - inward
- 頬骨の前後 (cheekbone depth) `face:CheekBoneZ` — + the cheekbones come forward / - sit back
- 頬の幅 (cheek width) `face:CheekW` — + the cheeks move outward (fuller face) / - inward (slimmer face)
- 頬の前後 (cheek depth) `face:CheekZ` — + the cheeks come forward / - sit back
- 頬の上下 (cheek height) `face:CheekY` — + the cheeks move up / - down

### 輪郭：顎 (Outline: jaw)

- 顎の下部上下 (lower jaw height) `face:ChinLowY` — + the jaw line under the chin moves up (shorter jaw) / - moves down (longer jaw)
- 顎の下部奥行 (lower jaw depth) `face:ChinLowZ` — + the jaw corners move outward and down (wider, squarer jaw) / - move in and up (slimmer jaw)
- 顎の上下 (chin height) `face:ChinY` — + the chin moves down: a longer lower face / - moves up: a shorter, rounder lower face
- 顎の幅 (chin width) `face:ChinW` — + the chin becomes wider / - narrower
- 顎の前後 (chin depth) `face:ChinZ` — + the chin comes forward / - sits back
- 顎の先上下 (chin tip height) `face:ChinTipY` — + the chin tip moves down (longer, pointed) / - moves up (shorter)
- 顎の先前後 (chin tip depth) `face:ChinTipZ` — + the chin tip comes forward / - sits back
- 顎の先幅 (chin tip width) `face:ChinTipW` — + the chin tip becomes wider (blunt) / - narrower (pointed)

### 輪郭：特殊表現 (Outline: shading)

- 特殊表現の強さ (shading strength) `param:faceDetailPower` — + facial shading becomes stronger / - fainter

### 輪郭：ほくろ (Outline: mole)

Mole type and colour: `param:moleId`, `param:moleColor`.

- 左右補正 (left/right) `param:moleX` — + the mole moves to the screen's right (the character's left) / - to the screen's left
- 上下補正 (up/down) `param:moleY` — + the mole moves up / - down
- サイズ補正 (size) `param:moleSize` — + the mole becomes larger / - smaller

### 眉 (Eyebrows)

- 髪の透過度 (hair transparency) `param:hairTransparency` — + the bangs become see-through so the eyes and brows show / - the bangs are opaque
- 眉の上下 (brow height) `face:EyebrowY` — + the eyebrows move up / - down
- 眉の横位置 (brow position) `face:EyebrowX` — + the eyebrows move outward (further apart) / - inward
- 眉の角度 (brow angle) `face:EyebrowRotZ` — + the outer ends go down and the inner ends up (troubled brows) / - the outer ends go up and the inner ends down (sharp, angry brows)
- 眉の内側形状 (inner brow shape) `face:EyebrowInForm` — + the inner half of each brow rises (inner end up) / - lowers (inner end down)
- 眉の外側形状 (outer brow shape) `face:EyebrowOutForm` — + the outer tips bend up / - bend down
- 眉の横幅 (brow length) `param:eyebrowWidth` — + the eyebrows become longer / - shorter
- 眉の縦幅 (brow thickness) `param:eyebrowHeight` — + the eyebrows become thicker / - thinner

### 目 (Eyes)

- 髪の透過度 (hair transparency) `param:hairTransparency` — + the bangs become see-through so the eyes and brows show / - the bangs are opaque
- 目の上下 (eye height) `face:EyeY` — + the eyes move up / - down
- 目の横位置 (eye position) `face:EyeX` — + the eyes move outward (further apart) / - inward
- 目の前後 (eye depth) `face:EyeZ` — + the eyes come forward (shallow-set) / - sit deeper
- 目の角度 (eye angle) `face:EyeTilt` — + the outer corners go down (droopy eyes) / - go up (upturned, cat-like eyes)
- 目の縦幅 (eye height size) `face:EyeH` — + the eyes become taller (round, big eyes) / - shorter (narrow eyes)
- 目の横幅 (eye width) `face:EyeW` — + the eyes become wider / - narrower

### 目：まつ毛 (Eyes: lashes)

- 上まつ毛の形状１ (upper lash shape 1) `face:EyelidsUpForm1` — + the inner-corner part of the upper lid moves down (the eye closes in at the inner corner) / - moves up
- 上まつ毛の形状２ (upper lash shape 2) `face:EyelidsUpForm2` — + the middle of the upper lid moves down (flatter top, sleepy eyes) / - moves up (rounder, wide-open top)
- 上まつ毛の形状３ (upper lash shape 3) `face:EyelidsUpForm3` — + the outer-corner part of the upper lid moves up (more open at the outer corner) / - moves down (heavier outer corner)
- 下まつ毛の形状１ (lower lash shape 1) `face:EyelidsLowForm1` — + the inner-corner point of the lower lid slides along the lid toward the eye's centre / - slides back toward the inner corner
- 下まつ毛の形状２ (lower lash shape 2) `face:EyelidsLowForm2` — + the middle of the lower lid moves up (narrower eye) / - moves down (taller eye)
- 下まつ毛の形状３ (lower lash shape 3) `face:EyelidsLowForm3` — + the outer-corner part of the lower lid moves up / - moves down

### 目：まぶた (Eyes: double eyelid)

- まぶたの横の位置 (horizontal position) `param:eyelidX` — + the double-eyelid line moves toward the inner corner / - toward the outer corner
- まぶたの縦の位置 (vertical position) `param:eyelidY` — + the line moves up / - down
- まぶたの回転 (rotation) `param:eyelidRotation` — + the line tilts (direction not confirmed) / - tilts the other way
- まぶたの大きさ (size) `param:eyelidSize` — + the line becomes larger / - smaller

### 目：瞳のプリセット (Eyes: iris preset)

Same values as the 目：瞳 and 目：瞳孔 tabs.

- 瞳の左右位置 (iris left/right) `param:irisX` — + the irises move toward the nose / - toward the outer corners
- 瞳の上下位置 (iris up/down) `param:irisY` — + the irises move up / - down
- 瞳の幅 (iris width) `param:irisWidth` — + the irises become wider / - narrower
- 瞳の高さ (iris height) `param:irisHeight` — + the irises become taller / - shorter
- 瞳孔の幅 (pupil width) `param:pupilWidth` — + the pupils become wider / - narrower
- 瞳孔の高さ (pupil height) `param:pupilHeight` — + the pupils become taller / - shorter

### 目：瞳 (Eyes: iris)

- 瞳の左右位置 (iris left/right) `param:irisX` — + the irises move toward the nose / - toward the outer corners
- 瞳の上下位置 (iris up/down) `param:irisY` — + the irises move up / - down
- 瞳の幅 (iris width) `param:irisWidth` — + the irises become wider / - narrower
- 瞳の高さ (iris height) `param:irisHeight` — + the irises become taller / - shorter

### 目：瞳孔 (Eyes: pupil)

- 瞳孔の幅 (pupil width) `param:pupilWidth` — + the pupils become wider / - narrower
- 瞳孔の高さ (pupil height) `param:pupilHeight` — + the pupils become taller / - shorter

### 目：瞳グラデ (Eyes: iris gradient)

- 目のグラデ位置 (gradient position) `param:eyeGradPosition` — + the iris gradient moves down (the top of the iris keeps the base colour) / - moves up
- 目のグラデサイズ (gradient size) `param:eyeGradSize` — + the gradient covers more of the iris / - less

### 目：ハイライト01 / 02 / 03 (Eyes: highlight 1 / 2 / 3)

The three tabs have the same sliders; select the highlight with `{"highlight":0}`,
`{"highlight":1}` or `{"highlight":2}`.

- ハイライトの上下位置 (up/down) `param:highlightY` — + the highlight moves up or down (direction not confirmed) / - the other way
- ハイライトの左右位置 (left/right) `param:highlightX` — + the highlight moves to the character's right (the screen's left) in both eyes / - to the character's left
- ハイライトの横幅 (width) `param:highlightWidth` — + the highlight becomes wider / - narrower
- ハイライトの縦幅 (height) `param:highlightHeight` — + the highlight becomes taller / - shorter
- ハイライトの傾き (tilt) `param:highlightTilt` — + the highlight tilts (direction not confirmed) / - tilts the other way

### 目：白目 (Eyes: whites)

No sliders. Colours: `param:whiteBaseColor`, `param:whiteSubColor`, `param:whiteSub2Color`.

### 鼻 (Nose)

- ハイライト強度 (highlight strength) `param:noseGloss` — + the nose highlight becomes stronger / - weaker
- 鼻先の高さ (nose tip height) `face:NoseTipH` — + the nose tip sticks out further / - is flatter
- 鼻の上下 (nose height position) `face:NoseY` — + the nose moves up / - down
- 鼻筋の高さ (bridge height) `face:NoseBridgeH` — + the nose bridge comes forward (higher bridge) / - sits back (lower bridge)

### 口 (Mouth)

- 口の上下 (mouth height position) `face:MouthY` — + the mouth moves up / - down
- 口の横幅 (mouth width) `face:MouthW` — + the mouth becomes wider / - narrower
- 口の前後 (mouth depth) `face:MouthZ` — + the mouth comes forward / - sits back
- 口の形状上 (upper lip) `face:MouthUpForm` — + the upper lip pushes forward (fuller) / - sits back (thinner)
- 口の形状下 (lower lip) `face:MouthLowForm` — + the lower lip pushes forward (fuller) / - sits back (thinner)
- 口の形状口角角度 (mouth corner angle) `face:MouthCornerFormRot` — + the corners turn up (smiling curve) / - turn down (frowning curve)
- 口の形状口角上下 (mouth corner height) `face:MouthCornerFormY` — + the corners move up (smiling) / - move down

### 耳 (Ears)

- 耳のサイズ (ear size) `face:EarSize` — + the ears become larger / - smaller (0.5 and below keep the base size)
- 耳の角度Y軸 (ear angle, Y axis) `face:EarRotY` — + the ears turn around the vertical axis (direction not confirmed; the ears are usually hidden by hair) / - turn the other way
- 耳の角度Z軸 (ear angle, Z axis) `face:EarRotZ` — + the ears tilt (direction not confirmed; the ears are usually hidden by hair) / - tilt the other way
- 耳の上部形状 (upper ear shape) `face:EarUpForm` — + the top of the ear stretches outward and up and becomes small and pointed (elf ears) / - stays rounded
- 耳の下部形状 (lower ear shape) `face:EarLowForm` — + the earlobes move down (longer lobes) / - move up

### 化粧：チーク (Makeup: blush)

- チークの横の位置 (horizontal position) `param:cheekX` — + the blush moves outward (toward the ears) / - inward (toward the nose)
- チークの縦の位置 (vertical position) `param:cheekY` — + the blush moves up / - down
- チークの回転 (rotation) `param:cheekRotation` — + the blush rotates (direction not confirmed) / - rotates the other way
- チークの大きさ (size) `param:cheekSize` — + the blush becomes larger / - smaller

### 化粧：アイシャドウ, 化粧：リップ (Makeup: eye shadow, lipstick)

No sliders. Type and colour are set with `POST /api/v1/creator/makeup`.

### 化粧：ペイント01 / 02 / 03 (Makeup: face paint 1 / 2 / 3)

Paint type and colour: `param:facePaint1Id`, `param:facePaint1Color` (2 and 3 likewise).

- 左右補正 (left/right) `param:facePaint1X` — + the paint moves to the screen's right (the character's left) / - to the screen's left
- 上下補正 (up/down) `param:facePaint1Y` — + the paint moves up / - down
- 回転 (rotation) `param:facePaint1Rotation` — + the paint rotates (direction not confirmed) / - rotates the other way
- サイズ補正 (size) `param:facePaint1Size` — + the paint becomes larger / - smaller

### 変形まとめ（顔） (All face shapes)

A tab that gathers the face shape sliders of the other tabs; it has no sliders of its own.

## Body (体)

### 肌と頭身 (Skin and proportions)

Skin colour and type are set with `POST /api/v1/creator/skin`.

- 身長 (height) `body:Height` — + taller / - shorter (the head keeps its size)
- 頭のサイズ (head size) `body:HeadSize` — + the head becomes larger / - smaller
- 肉感の強さ (flesh shading) `param:bodyDetailPower` — + body shading (fleshiness) becomes stronger / - weaker
- ツヤの強さ (skin shine) `param:skinShinePower` — + the skin shines more / - less

### 胸 (Breasts)

- 胸のサイズ (size) `body:BustSize` — + the breasts become larger / - smaller
- 胸の上下位置 (height) `body:BustY` — + the breasts sit higher / - lower
- 胸の左右開き (spread) `body:BustRotX` — + the breasts point further to the sides / - point more to the front and together
- 胸の左右位置 (spacing) `body:BustX` — + the breasts sit further apart / - closer together
- 胸の上下角度 (vertical angle) `body:BustRotY` — + the breasts point more upward / - more downward
- 胸の尖り (pointiness) `body:BustSharp` — + the breasts become pointier (cone-shaped) / - rounder
- 胸の形状 (shape) `body:BustForm1` — + the upper curve flattens / - becomes fuller
- 胸の根本の角度 (base angle) `body:BustForm2` — + the lower curve lifts and pushes forward / - hangs and flattens
- 胸の柔らかさ (softness) `param:bustSoftness` — + the breasts sway more / - are firmer and sway less
- 胸の重さ (weight) `param:bustWeight` — + the breasts are heavier and hang lower / - lighter and sit higher

### 胸：乳首 (Breasts: nipples)

Nipple type and colour: `param:nipId`, `param:nipColor`.

- 乳輪の膨らみ (areola puffiness) `body:AreolaBulge` — + the areolae swell / - are flat
- 乳首の太さ (nipple size) `body:NipWeight` — + the nipples become larger / - smaller
- 乳首立ち (nipple erection) `body:NipStand` — + the nipples stick out further / - are flatter
- 乳輪の大きさ (areola size) `param:areolaSize` — + the areolae become larger / - smaller

### 上半身 (Upper body)

- 首のサイズ (neck size) `body:NeckSize` — + the neck becomes thicker / - thinner
- 首元の上下位置 (neck length) `body:NeckY` — + the neck becomes longer / - shorter
- 胴体肩周りの幅 (shoulder-area width) `body:BodyShoulderW` — + the upper chest and shoulder area become wider / - narrower
- 胴体肩周りの奥 (shoulder-area depth) `body:BodyShoulderZ` — + the upper chest becomes thicker front to back / - thinner
- 胴体上の幅 (chest width) `body:BodyUpW` — + the rib cage becomes wider / - narrower
- 胴体上の奥 (chest depth) `body:BodyUpZ` — + the rib cage becomes thicker front to back / - thinner
- 胴体下の幅 (lower torso width) `body:BodyLowW` — + the lower torso becomes wider / - narrower
- 胴体下の奥 (lower torso depth) `body:BodyLowZ` — + the lower torso becomes thicker front to back / - thinner

### 上半身：腕 (Upper body: arms)

- 肩の幅 (shoulder width) `body:ShoulderW` — + the shoulders become wider / - narrower
- 肩のサイズ (shoulder size) `body:ShoulderZ` — + the shoulders become thicker and rounder / - thinner
- 上腕のサイズ (upper arm size) `body:ArmUpSize` — + the upper arms become thicker / - thinner
- ヒジのサイズ (elbow size) `body:ElbowSize` — + the elbows become thicker / - thinner
- 前腕のサイズ (forearm size) `body:ArmFront` — + the forearms become thicker / - thinner
- 手首のサイズ (wrist size) `body:WristSize` — + the wrists become thicker / - thinner
- 手と指の太さ (hand and finger size) `body:HandW` — + the hands become larger / - smaller

### 下半身 (Lower body)

- ウエストの上下位置 (waist height) `body:WaistY` — + the waist (narrowest point) sits higher / - lower
- 腹部のサイズ (belly size) `body:Belly` — + the belly becomes rounder and sticks out / - is flat
- 腰上の幅 (waist width) `body:WaistUpW` — + the waist becomes wider / - narrower (more cinched)
- 腰上の奥 (waist depth) `body:WaistUpZ` — + the waist becomes thicker front to back / - thinner
- 腰下の幅 (hip width) `body:WaistLowW` — + the hips (pelvis) become wider / - narrower
- 腰下の奥 (pelvis depth) `body:WaistLowZ` — + the pelvis becomes thicker front to back / - thinner
- 尻のサイズ (buttocks size) `body:HipSize` — + the buttocks become larger / - smaller
- 尻の上下角度 (buttocks angle) `body:HipRotX` — + the buttocks sit higher (perkier) / - lower
- 尻の縦幅 (buttocks height) `body:HipSizeY` — + the buttocks become taller / - shorter

### 下半身：脚 (Lower body: legs)

- 太もも上のサイズ (upper thigh size) `body:ThighUpSize` — + the upper thighs become thicker / - thinner
- 太もも下のサイズ (lower thigh size) `body:ThighLowSize` — + the lower thighs (above the knee) become thicker / - thinner
- ヒザのサイズ (knee size) `body:KneeSize` — + the knees become larger / - smaller
- ふくらはぎのサイズ (calf size) `body:Calf` — + the calves become thicker / - thinner
- 足首のサイズ (ankle size) `body:AnkleSize` — + the ankles become thicker / - thinner

### 柔らかさ (Softness)

- 胸の柔らかさ (breast softness) `param:bustSoftness` — + the breasts sway more / - are firmer and sway less
- 胸の重さ (breast weight) `param:bustWeight` — + the breasts are heavier and hang lower / - lighter and sit higher
- 上腕の柔らかさ (upper arm softness) `param:upperArmSoftness` — + the upper arms sway more / - less
- 腹部の柔らかさ (belly softness) `param:bellySoftness` — + the belly sways more / - less
- 腰の柔らかさ (waist softness) `param:waistSoftness` — + the waist sways more / - less
- 尻の柔らかさ (buttocks softness) `param:hipSoftness` — + the buttocks sway more / - less
- 太ももの柔らかさ (thigh softness) `param:thighSoftness` — + the thighs sway more / - less

### 陰毛 (Pubic hair)

No sliders. Type and colour: `param:underhairId`, `param:underhairColor`.

### 日焼け01, 日焼け02 (Tan lines 1, 2)

No sliders. Types: `sunburnUpId`, `sunburnDownId` in `POST /api/v1/creator/skin`;
colours: `param:sunburnUpColor`, `param:sunburnDownColor`.

### 化粧：手のネイル, 化粧：足のネイル (Makeup: fingernails, toenails)

Nail type and colours: `param:nailId`, `param:nailColor1` to `nailColor3`
(with `{"foot":true}` for toenails).

- ネイルのツヤの強さ (nail gloss) `param:nailGloss` `{"foot":false}` or `{"foot":true}` — + the nails look glossier / - more matte

### 化粧：ペイント01 / 02 / 03 (Makeup: body paint 1 / 2 / 3)

Paint type and colour: `param:bodyPaint1Id`, `param:bodyPaint1Color` (2 and 3 likewise).

- 左右補正 (left/right) `param:bodyPaint1X` — + the paint moves to the screen's right (the character's left) / - to the screen's left
- 上下補正 (up/down) `param:bodyPaint1Y` — + the paint moves up / - down
- 回転 (rotation) `param:bodyPaint1Rotation` — + the paint rotates (direction not confirmed) / - rotates the other way
- サイズ補正 (size) `param:bodyPaint1Size` — + the paint becomes larger / - smaller

### 簡易操作 (Quick adjustments)

Each slider moves several shape values together; through the API, set those values
one by one.

- 胴体横幅 (torso width) `body:NeckSize, BodyShoulderW, BodyUpW, BodyLowW` — + the neck, shoulder area, chest and lower torso all become wider / - narrower
- 胴体厚み (torso depth) `body:BodyShoulderZ, BodyUpZ, BodyLowZ, Belly` — + the torso becomes thicker and the belly rounder / - thinner
- 下腹部横幅 (lower abdomen width) `body:WaistUpW, WaistLowW` — + the waist and hips become wider / - narrower
- 下腹部厚み (lower abdomen depth) `body:WaistUpZ, WaistLowZ, HipSize` — + the waist and pelvis thicken and the buttocks grow / - the reverse
- 腕の太さ (arm thickness) `body:ShoulderW, ShoulderZ, ArmUpSize, ElbowSize, ArmFront, WristSize, HandW` — + the whole arm, shoulder to hand, becomes thicker / - thinner
- 脚の太さ (leg thickness) `body:ThighUpSize, ThighLowSize, KneeSize, Calf, AnkleSize` — + the whole leg, thigh to ankle, becomes thicker / - thinner

### 変形まとめ（身体） (All body shapes)

A tab that gathers the body sliders of the other tabs; it has no sliders of its own.

## Not shown in the maker

- `face:NoseBase` — + the base of the nose (nostrils) moves up / - moves down
