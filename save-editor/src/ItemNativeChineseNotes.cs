using System;
using System.Collections.Generic;

namespace SealSaveEditor
{
    public static class ItemNativeChineseNotes
    {
        // Reviewed natural item annotations: 189 specific notes, 14 names
        // corroborated by s_Item, and 5 role-specific book labels.
        // Provenance: original table annotation, not proof of a localized retail title.
        // Deliberately exclude generic gear/build notes, tiers, creature families,
        // element shorthand and quest codes. Alias whitespace is meaningful.
        private static readonly HashSet<string> Aliases = new HashSet<string>(StringComparer.Ordinal)
        {
            "shouzhang", "xiongnuchangdao", "pjduanjian", "shourentiezhua", "lianxinqiansuojia",
            "drughp_1", "drughp_2", "drughp_3", "drughp_4", "drugmp_1",
            "drugmp_2", "drugmp_3", "drugmp_4", "drug_1", "drug_2",
            "ore1", "ore2", "ore3", "ore4", "ore5",
            "gem1", "gem2", "gem3", "gem4", "gem5",
            "tooth_she", "skin_she", "skin_snakeking", "bone_she", "meat_she",
            "skin_grass", "tooth_tree", "skin_tree", "bone_tree", "tooth_sundew",
            "skin_sundew", "bone_sundew", "skin_spider", "skin_spiderking", "bone_spider",
            "tooth_dragonfly", "skin_dragonfly", "bone_dragonfly", "meat_dragonfly", "tooth_bigmouth",
            "skin_bigmouth", "tooth_fox", "skin_fox", "bone_fox", "meat_fox",
            "tooth_leopard", "skin_leopard", "bone_leopard", "meat_leopard", "tooth_tiger",
            "skin_tiger", "bone_tiger", "meat_tiger", "tooth_toxic", "skin_toxic",
            "tooth_doublehead", "skin_doublehead", "bone_doublehead", "meat_doublehead", "tooth_phasmia",
            "skin_phasmia", "bone_phasmia", "tooth_thunder", "skin_thunder", "bone_thunder",
            "meat_thunder", "tooth_bat", "skin_bat", "bone_bat", "tooth_guardian",
            "skin_guardian", "bone_guardian", "tooth_direfox", "skin_direfox", "bone_direfox",
            "tooth_zombie", "skin_zombie", "bone_zombie", "meat_zombie", "tooth_rhino",
            "skin_rhino", "bone_rhino", "meat_rhino", "skin_elf", "bone_elf",
            "tooth_taurean", "skin_taurean", "bone_taurean", "meat_taurean", "skin_imp",
            "bone_imp", "tooth_evocator", "skin_evocator", "bone_evcator", "meat_evocator",
            "bone_monster", "skin_monster", "tooth_danghu", "skin_danghu", "bone_danghu",
            "gem_mudworm", "tooth_boar", "skin_boar", "bone_boar", "meat_boar",
            "gem_donghai", "gem_huangdi", "tooth_icefrog", "skin_icefrog", "bone_icefrog",
            "tooth_yanwei", "skin_yanwei", "bone_yanwei", "tooth_jingwei", "skin_jingwei",
            "bone_jingwei", "skillbook_1", "skillbook_2", "skillbook_3", "skillbook_4",
            "skillbook_5", "gold", "z_shipian", "z_shewangdan", "z_baozidan",
            "z_shewangjieyao", "z_zhanlingding", "z_hongnifuzi", "z_yucibaoguo", "z_gouqixuelian",
            "z_pengjiajieyao", "z_lingzhucaojieyao", "z_changmingsuo", "z_xiangshixinwu", "z_xianglu",
            "z_niwawa", "z_zhubao", "z_hanxingtoulu", "z_muyeyaowan", "z_muyeyaofen",
            "z_buwawa", "z_sldling", "z_quxiefajuan", "z_shiwenjieju", "z_xiuhuazhen",
            "z_huoshoulinpian", "z_chiyouxiang", "z_xiangshixinwu2", "z_tongyaoshi", "z_tongxinglingpai",
            "z_xunxiang", "z_xionghuang", "z_wanduxin", "z_shourengu", "z_duchongshiti",
            "z_chiyanshoujiao", "z_putongshujian", "z_kunwu", "z_bingjing", "z_huoyu",
            "z_jinchuangyao", "z_qiulongjiao", "z_hujiaorou", "z_zijinghua", "z_shujian",
            "z_yangpi1", "z_yangpi2", "z_yangpi3", "z_yangpi4", "z_cybook",
            "z_shibei", "z_shike", "z_jrshibei", "z_youhuo", "z_youhuo2",
            "z_dpbshibei", "z_podunpai", "z_pjguji", "z_huainanzi", "z_shanhaijing",
            "z_hugujiu", "z_baihuaniang", "z_houjiu", "z_hongnijiu", "z_qiannianrenshen",
            "z_houguo", "z_lingzhucao", "z_duzhucao", "z_huitiancao", "z_hualingcao",
            "z_caoyao", "z_shedujieyao", "z_huadujieyao", "z_juhua1", "z_juhua2",
            "z_yaohuneidan", "z_shitouxin ", "z_xiangshi", "z_huoshen", "z_hongfeicui",
            "z_yunxiayu", "z_kunwutong", "z_xiongpi",
        };

        public static bool IsTrusted(string alias)
        {
            return alias != null && Aliases.Contains(alias);
        }
    }
}
