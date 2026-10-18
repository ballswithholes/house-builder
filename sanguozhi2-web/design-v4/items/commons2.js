'use strict';
// Additional common / uncommon weapons (same row format as commons.js)
const MORE = {
  han: [
    ['lance', 'c', 'han_shuo', '马槊', null, '长杆、八棱槊头、杆尾铜鐏', '汉末骑将的长矟（槊），冲阵之兵（「横槊赋诗」为后世之说）。', 'history', []],
    ['lance', 'u', 'han_shuo_u', '丈八马矟', null, '超长矟杆、矟头错银云纹', '长矟，汉魏骑兵冲阵之兵（游戏取舍）。', 'liberty', []],
    ['shortji', 'u', 'han_shouji_u', '鎏金手戟', null, '戟枝鎏金、柄缠鲛皮', '贵重的手戟（游戏取舍）。', 'liberty', []],
    ['bow', 'u', 'han_qibei', '漆背角弓', null, '弓背髹朱漆、弓弣缠丝', '精制的汉式复合弓（考工记·弓人记六材）。', 'history', ['考工记·弓人']],
  ],
  nanman: [
    ['mace', 'c', 'nm_jili', '木蒺藜棒', null, '硬木棒头嵌兽牙', '南中木棒（游戏取舍）。', 'liberty', []],
  ],
  wa: [
    ['spear', 'u', 'wa_mao_u', '倭铁长矛', null, '长铁矛头、朱漆杆', '（游戏取舍）', 'liberty', []],
    ['ge', 'u', 'wa_guangge', '广形铜戈', null, '宽大的祭祀铜戈、刃面打磨如镜', '弥生后期北九州的大型祭祀铜戈（考古）。', 'archaeology', []],
  ],
  yi: [
    ['bow', 'u', 'yi_changgong', '夷洲长竹弓', null, '长竹弓、弓梢缠贝珠', '（游戏取舍）', 'liberty', []],
    ['spear', 'u', 'yi_lujiao_u', '鹿角长矛', null, '双叉鹿角矛头、羽饰', '（游戏取舍）', 'liberty', []],
  ],
  korea: [
    ['ji', 'c', 'ko_ji', '东夷铁戟', null, '短枝铁戟', '朝鲜半岛出土铁戟（考古）。', 'archaeology', []],
    ['handaxe', 'c', 'ko_fu', '伽倻铁斧', null, '有銎铁斧、短柄', '加耶古墓多出铁斧（考古）。', 'archaeology', []],
  ],
  steppe: [
    ['mace', 'c', 'st_chui', '草原铁锤', null, '球形铁锤头、皮绳腕套', '（游戏取舍）', 'liberty', []],
    ['dao', 'u', 'st_hudao_u', '金饰胡刀', null, '刀柄包金、柄首狼头', '（游戏取舍）', 'liberty', []],
  ],
  tarim: [
    ['bow', 'u', 'ta_gong_u', '龟兹角弓', null, '角弓、弓囊织锦', '（游戏取舍）', 'liberty', []],
    ['spear', 'u', 'ta_mao_u', '于阗长矛', null, '长矛、矛下系玉坠', '（游戏取舍）', 'liberty', []],
  ],
  kushan: [
    ['lance', 'u', 'ku_lance_u', '贵霜金箍骑矛', null, '长矛杆三道金箍、紫缨', '（游戏取舍）', 'liberty', []],
    ['bow', 'u', 'ku_gong_u', '大宛强弓', null, '粗大的复合弓', '（游戏取舍）', 'liberty', []],
  ],
  seasia: [
    ['spear', 'u', 'sa_mao_u', '林邑长铁矛', null, '长铁矛、杆缠金线', '（游戏取舍）', 'liberty', []],
    ['throw', 'c', 'sa_biao', '南海投枪', null, '竹杆投枪一束', '（游戏取舍）', 'liberty', []],
    ['crossbow', 'c', 'sa_nu', '雒越铜镞弩', null, '木弩、青铜三棱镞', '越南古螺城遗址出土数以万计的青铜弩镞（考古）。', 'archaeology', ['Cổ Loa citadel finds']],
  ],
  india: [
    ['handaxe', 'c', 'in_parashu', '帕拉苏斧', 'paraśu', '新月形斧刃、长柄', '古印度战斧，史诗中持斧罗摩以之闻名。', 'myth', ['摩诃婆罗多']],
    ['bow', 'c', 'in_jiaogong', '天竺角弓', null, '短反曲弓', '（游戏取舍）', 'liberty', []],
  ],
  persia: [
    ['handaxe', 'c', 'pe_tabar', '安息战斧', 'tabar', '窄刃战斧、长柄', '伊朗系战斧（游戏取舍）。', 'liberty', []],
    ['sword', 'c', 'pe_duanjian', '安息短剑', null, '缚于大腿外侧的短剑、四耳剑鞘', '帕提亚贵族将短剑缚于大腿（雕像与岩刻）。', 'archaeology', []],
  ],
  arab: [
    ['lance', 'c', 'ar_qimao', '阿拉伯骑矛', null, '长竹骑矛', '（游戏取舍）', 'liberty', []],
    ['throw', 'c', 'ar_biao', '阿拉伯短标', null, '三支短标枪', '（游戏取舍）', 'liberty', []],
  ],
  africa: [
    ['spear', 'u', 'af_mao_u', '金箍长矛', null, '乌木杆三道金箍', '（游戏取舍）', 'liberty', []],
    ['bow', 'c', 'af_gong', '努比亚弓', null, '单体木弓、弓梢象牙', '努比亚人以善射闻名，古埃及称其地为「弓之地」（Ta-Seti）。', 'history', []],
    ['throw', 'c', 'af_biao', '阿克苏姆投枪', null, '一束细铁投枪', '（游戏取舍）', 'liberty', []],
  ],
  roman: [
    ['gladius', 'c', 'ro_mainz', '美因茨式短剑', 'gladius (Mainz type)', '腰身收窄、长三角剑尖', '1 世纪的美因茨式短剑（考古）。', 'archaeology', []],
    ['throw', 'c', 'ro_lancea', '轻标枪', 'lancea', '细长的轻标枪三支', '辅助军的轻标枪。', 'history', []],
    ['spear', 'u', 'ro_praetorian', '禁卫军长矛', null, '长矛、矛下饰银环', '（游戏取舍）', 'liberty', []],
    ['handaxe', 'c', 'ro_securis', '扈从之斧', 'securis', '束棒中的单刃斧', '侍从官扈从（lictor）所持束棒中插一斧，象征生杀之权。', 'history', []],
    ['cloth', 'c', 'ro_subarmalis', '衬甲', 'subarmalis', '絮棉衬甲、肩部垂皮条（pteruges）', '穿在金属甲内的絮棉衬衣，边缘垂皮条。', 'archaeology', []],
    ['bow', 'u', 'ro_hamian', '哈米人复合弓', null, '骨片加固的复合弓', '叙利亚哈米人弓手曾驻守哈德良长城卡尔沃兰要塞（铭文）。', 'archaeology', ['Carvoran inscriptions']],
  ],
  celt: [
    ['spatha', 'u', 'ce_renxing', '人形柄长剑', null, '剑柄铸成人形（双臂双腿为剑格与剑首）', '拉坦诺时期的人形柄剑（考古）。', 'archaeology', []],
    ['spear', 'u', 'ce_kuanmao', '宽刃战矛', null, '波浪形宽刃矛头', '（考古）', 'archaeology', []],
    ['handaxe', 'c', 'ce_fu', '凯尔特战斧', null, '有銎铁斧', '（游戏取舍）', 'liberty', []],
  ],
  german: [
    ['spear', 'u', 'ge_framea_u', '镶银弗雷亚矛', null, '矛銎镶银丝', '（游戏取舍）', 'liberty', []],
    ['bow', 'c', 'ge_gong', '紫杉长弓', null, '紫杉木单体长弓', '北欧沼泽祭坑出土紫杉木弓（如丹麦尼达姆，稍晚，考古）。', 'archaeology', ['Nydam bog']],
    ['handaxe', 'u', 'ge_fu_u', '铁刃首领斧', null, '斧刃錾纹、柄缠皮', '（游戏取舍）', 'liberty', []],
  ],
  sarmatian: [
    ['lance', 'u', 'sm_kontos_u', '金箍萨尔马提亚长矛', null, '矛杆金箍、红缨', '（游戏取舍）', 'liberty', []],
    ['sword', 'c', 'sm_duanjian', '环首短剑', null, '环形剑首的短剑', '萨尔马提亚环首剑（考古）。', 'archaeology', []],
  ],
  moor: [
    ['throw', 'u', 'mo_biao_u', '毛里铁头标枪', null, '铁头标枪一束、杆缠紫皮', '（游戏取舍）', 'liberty', []],
    ['sword', 'c', 'mo_jian', '毛里短剑', null, '短直剑、皮鞘', '（游戏取舍）', 'liberty', []],
    ['bow', 'c', 'mo_gong', '盖图利亚弓', null, '短木弓', '（游戏取舍）', 'liberty', []],
  ],
};
module.exports = { MORE };
