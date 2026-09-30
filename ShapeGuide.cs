#nullable disable
using System.Collections.Generic;
using System.Linq;

namespace Amanatsu.AiExtension;

// What raising and lowering each face/body shape slider does (0..1, default 0.5).
// Derived from the game's own shape data (lib/chara/list/customshape.unity3d: which bones each slider
// drives and their position/rotation/scale over the slider range). Positions and scales are read
// directly; rotations were checked with captures in the game (except the two ear rotations).
internal static class ShapeGuide
{
    static readonly (string name, string up, string down)[] Face =
    {
        ("FaceBaseW", "the whole face becomes wider", "the face becomes narrower"),
        ("FaceUpZ", "the upper face (forehead and brow area) comes forward", "the upper face sits back"),
        ("FaceUpY", "the upper face becomes taller and the nose bridge longer", "the upper face becomes shorter"),
        ("FaceUpSize", "the upper face becomes larger", "the upper face becomes smaller"),
        ("FaceLowZ", "the lower face (mouth and jaw) comes forward", "the lower face sits back"),
        ("FaceLowW", "the lower face becomes wider", "the lower face becomes narrower"),
        ("ChinLowY", "the lower jaw line moves up (shorter jaw)", "the lower jaw line moves down (longer jaw)"),
        ("ChinLowZ", "the jaw corners move outward and down (wider, squarer jaw)", "the jaw corners move in and up (slimmer jaw)"),
        ("ChinY", "the chin moves down: a longer lower face", "the chin moves up: a shorter, rounder lower face"),
        ("ChinW", "the chin becomes wider", "the chin becomes narrower"),
        ("ChinZ", "the chin comes forward", "the chin sits back"),
        ("ChinTipY", "the chin tip moves down (longer, pointed)", "the chin tip moves up (shorter)"),
        ("ChinTipZ", "the chin tip comes forward", "the chin tip sits back"),
        ("ChinTipW", "the chin tip becomes wider (blunt)", "the chin tip becomes narrower (pointed)"),
        ("CheekBoneW", "the cheekbones move outward (wider)", "the cheekbones move inward"),
        ("CheekBoneZ", "the cheekbones come forward", "the cheekbones sit back"),
        ("CheekW", "the cheeks move outward (fuller face)", "the cheeks move inward (slimmer)"),
        ("CheekZ", "the cheeks come forward", "the cheeks sit back"),
        ("CheekY", "the cheeks move up", "the cheeks move down"),
        ("EyebrowY", "the eyebrows move up", "the eyebrows move down"),
        ("EyebrowX", "the eyebrows move outward (further apart)", "the eyebrows move inward (closer together)"),
        ("EyebrowRotZ", "the outer ends of the eyebrows go down and the inner ends up (troubled, sad brows)", "the outer ends go up and the inner ends down (sharp, angry brows)"),
        ("EyebrowInForm", "the inner half of each eyebrow rises (inner end up)", "the inner half lowers (inner end down)"),
        ("EyebrowOutForm", "the outer tips of the eyebrows bend up", "the outer tips bend down"),
        ("EyelidsUpForm1", "the inner-corner part of the upper lid moves down (eye closes in at the inner corner)", "it moves up (more open at the inner corner)"),
        ("EyelidsUpForm2", "the middle of the upper lid moves down (lower, flatter top: sleepier eyes)", "the middle of the upper lid moves up (rounder, more open top)"),
        ("EyelidsUpForm3", "the outer-corner part of the upper lid moves up (more open at the outer corner)", "it moves down (heavier outer corner)"),
        ("EyelidsLowForm1", "the inner-corner point of the lower lid slides along the lid toward the eye's centre", "it slides back toward the inner corner"),
        ("EyelidsLowForm2", "the middle of the lower lid moves up (narrower eye)", "the middle of the lower lid moves down (taller eye)"),
        ("EyelidsLowForm3", "the outer-corner part of the lower lid moves up", "it moves down"),
        ("EyeY", "the eyes move up", "the eyes move down"),
        ("EyeX", "the eyes move outward (further apart)", "the eyes move inward (closer together)"),
        ("EyeZ", "the eyes come forward (shallow-set)", "the eyes sit deeper"),
        ("EyeTilt", "the outer corners go down (droopy eyes)", "the outer corners go up (upturned, cat-like eyes)"),
        ("EyeH", "the eyes become taller (rounder, bigger)", "the eyes become shorter (narrower slits)"),
        ("EyeW", "the eyes become wider", "the eyes become narrower"),
        ("NoseTipH", "the nose tip sticks out further", "the nose tip is flatter"),
        ("NoseY", "the nose moves up", "the nose moves down"),
        ("NoseBridgeH", "the nose bridge comes forward (higher bridge)", "the nose bridge sits back (lower bridge)"),
        ("MouthY", "the mouth moves up", "the mouth moves down"),
        ("MouthW", "the mouth becomes wider", "the mouth becomes narrower"),
        ("MouthZ", "the mouth comes forward", "the mouth sits back"),
        ("MouthUpForm", "the upper lip pushes forward (fuller upper lip)", "the upper lip sits back (thinner)"),
        ("MouthLowForm", "the lower lip pushes forward (fuller lower lip)", "the lower lip sits back (thinner)"),
        ("MouthCornerFormRot", "the mouth corners turn up (smiling curve)", "the mouth corners turn down (frowning curve)"),
        ("MouthCornerFormY", "the mouth corners move up (smiling)", "the mouth corners move down"),
        ("EarSize", "the ears become larger", "the ears become smaller (0.5 and below keeps the base size)"),
        ("EarRotY", "the ears turn around the vertical axis (which way is not confirmed; the ears are usually hidden by hair)", "the ears turn the other way"),
        ("EarRotZ", "the ears tilt (which way is not confirmed; the ears are usually hidden by hair)", "the ears tilt the other way"),
        ("EarUpForm", "the top of the ear moves out and up and becomes smaller (pointed, elf-like)", "the top of the ear stays rounded"),
        ("EarLowForm", "the earlobes move down (longer lobes)", "the earlobes move up"),
        ("NoseBase", "the base of the nose (nostrils) moves up", "it moves down"),
        ("FaceSize", "the face becomes larger (0.5 and below keeps the base size)", "the face becomes smaller"),
        ("FaceY", "the face parts move up on the head", "the face parts move down"),
        ("HeadSize", "the head (hair base) is at full size", "the head becomes slightly smaller"),
    };

    static readonly (string name, string up, string down)[] Body =
    {
        ("Height", "taller", "shorter (the head keeps its size)"),
        ("HeadSize", "the head becomes larger", "the head becomes smaller"),
        ("NeckSize", "the neck becomes thicker", "the neck becomes thinner"),
        ("BustSize", "the breasts become larger", "the breasts become smaller"),
        ("BustY", "the breasts sit higher", "the breasts sit lower"),
        ("BustRotX", "the breasts point further to the sides (wider apart)", "the breasts point more to the front and together"),
        ("BustX", "the breasts sit further apart", "the breasts sit closer together"),
        ("BustRotY", "the breasts point more upward", "the breasts point more downward"),
        ("BustSharp", "the breasts become pointier (cone-shaped)", "the breasts become rounder"),
        ("BustForm1", "the upper curve of the breasts flattens", "the upper curve becomes fuller"),
        ("BustForm2", "the lower curve lifts and pushes forward", "the lower curve hangs and flattens"),
        ("AreolaBulge", "the areolae swell (puffier)", "the areolae are flat"),
        ("NipWeight", "the nipples become larger", "the nipples become smaller"),
        ("NipStand", "the nipples stick out further", "the nipples are flatter"),
        ("BodyShoulderW", "the upper chest and shoulders become wider", "they become narrower"),
        ("BodyShoulderZ", "the upper chest becomes thicker front to back", "it becomes thinner"),
        ("BodyUpW", "the chest (rib cage) becomes wider", "it becomes narrower"),
        ("BodyUpZ", "the chest becomes thicker front to back", "it becomes thinner"),
        ("BodyLowW", "the lower torso becomes wider", "it becomes narrower"),
        ("BodyLowZ", "the lower torso becomes thicker front to back", "it becomes thinner"),
        ("WaistY", "the waist (narrowest point) sits higher", "the waist sits lower"),
        ("Belly", "the belly becomes rounder and sticks out", "the belly is flat"),
        ("WaistUpW", "the waist becomes wider", "the waist becomes narrower (more cinched)"),
        ("WaistUpZ", "the waist becomes thicker front to back", "it becomes thinner"),
        ("WaistLowW", "the hips (pelvis) become wider", "they become narrower"),
        ("WaistLowZ", "the pelvis becomes thicker front to back", "it becomes thinner"),
        ("HipSize", "the buttocks become larger", "the buttocks become smaller"),
        ("HipRotX", "the buttocks sit higher (perkier)", "the buttocks sit lower"),
        ("ThighUpSize", "the upper thighs become thicker", "they become thinner"),
        ("ThighLowSize", "the lower thighs (above the knee) become thicker", "they become thinner"),
        ("Calf", "the calves become thicker", "they become thinner"),
        ("KneeSize", "the knees become larger", "the knees become smaller"),
        ("AnkleSize", "the ankles become thicker", "they become thinner"),
        ("ShoulderW", "the shoulders become wider", "the shoulders become narrower"),
        ("ShoulderZ", "the shoulders become thicker (rounder)", "they become thinner"),
        ("ArmUpSize", "the upper arms become thicker", "they become thinner"),
        ("ElbowSize", "the elbows become thicker", "they become thinner"),
        ("ArmFront", "the forearms become thicker", "they become thinner"),
        ("WristSize", "the wrists become thicker", "they become thinner"),
        ("HandW", "the hands become larger", "the hands become smaller"),
        ("NeckY", "the neck becomes longer", "the neck becomes shorter"),
        ("HipSizeY", "the buttocks become taller (larger vertically)", "they become shorter"),
    };

    internal static object All() => new
    {
        note = "Shape sliders run 0..1 with 0.5 as the neutral value; values outside 0..1 need slider-unlock.",
        face = Face.Select(x => new { name = x.name, increase = x.up, decrease = x.down }).ToArray(),
        body = Body.Select(x => new { name = x.name, increase = x.up, decrease = x.down }).ToArray()
    };
}
