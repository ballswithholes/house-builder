// 剧本：公元 190 年 · 董卓专横（反董卓联盟前夕）
// 数据以文本表保存，便于直接修改。
namespace Sanguo
{
    public static class ScenarioData
    {
        public const int StartYear = 190;
        public const int StartMonth = 1;
        public const string Title = "董卓专横";
        public const string Intro = "中平六年，灵帝驾崩。西凉董卓率军入京，废少帝、立献帝，专擅朝政。\n关东诸侯愤然而起，天下群雄并立。\n你将选择一位君主，逐鹿中原，一统天下。";

        // id | 名称 | 经度 | 纬度 | 土地 | 产业 | 町 | 人口 | 金 | 粮
        public static readonly string[] Cities =
        {
            "beiping|北平|116.4|39.9|420|300|320|380000|600|9000",
            "nanpi|南皮|116.7|38.0|520|380|360|520000|900|14000",
            "jinyang|晋阳|112.5|37.9|380|300|300|340000|500|8000",
            "ye|邺|114.3|36.3|620|520|480|720000|1200|18000",
            "pingyuan|平原|116.4|37.2|360|260|240|300000|400|6000",
            "beihai|北海|118.8|36.7|400|360|300|380000|500|8000",
            "puyang|濮阳|115.0|35.7|440|380|320|420000|600|10000",
            "chenliu|陈留|114.6|34.8|460|420|340|450000|700|10000",
            "xiaopei|小沛|116.9|34.7|340|280|240|260000|400|6000",
            "xiapi|下邳|118.0|34.3|500|460|400|560000|900|13000",
            "xuchang|许昌|113.8|34.0|560|500|420|600000|900|14000",
            "luoyang|洛阳|112.4|34.6|600|700|640|900000|2000|20000",
            "changan|长安|108.9|34.3|640|620|600|860000|1800|22000",
            "tianshui|天水|105.7|34.6|360|240|260|280000|400|7000",
            "wuwei|武威|102.6|37.9|320|220|240|220000|400|6000",
            "hanzhong|汉中|107.0|33.1|440|340|320|380000|600|11000",
            "wan|宛|112.5|33.0|540|460|400|580000|900|14000",
            "runan|汝南|114.4|33.0|500|420|360|520000|800|13000",
            "shouchun|寿春|116.8|32.6|520|440|380|500000|800|13000",
            "guangling|广陵|119.4|32.4|420|400|320|380000|600|9000",
            "lujiang|庐江|117.3|31.5|400|340|300|340000|500|9000",
            "jianye|建业|118.8|32.0|480|480|400|460000|800|11000",
            "wu|吴|120.6|31.3|460|460|360|420000|700|11000",
            "kuaiji|会稽|120.6|30.0|380|360|300|320000|500|8000",
            "xiangyang|襄阳|112.1|32.0|560|560|480|640000|1200|16000",
            "jiangxia|江夏|114.3|30.6|440|380|320|380000|600|10000",
            "jiangling|江陵|112.2|30.3|520|480|400|520000|900|14000",
            "changsha|长沙|113.0|28.2|440|360|320|380000|600|10000",
            "wuling|武陵|111.7|29.0|340|240|240|260000|300|7000",
            "lingling|零陵|111.6|26.4|320|220|220|240000|300|7000",
            "guiyang|桂阳|113.0|25.8|300|220|200|220000|300|6000",
            "yuzhang|豫章|115.9|28.7|380|300|280|320000|400|8000",
            "zitong|梓潼|105.1|31.6|360|260|260|260000|400|8000",
            "chengdu|成都|104.1|30.7|640|560|520|780000|1500|22000",
            "jiangzhou|江州|106.5|29.6|420|320|300|340000|500|10000",
            "yongan|永安|109.5|31.0|320|240|240|220000|300|7000",
            "jianning|建宁|103.8|25.5|320|200|200|240000|300|7000",
            "yunnan|云南|101.5|24.5|300|180|180|200000|300|6000",
        };

        public static readonly string[] Links =
        {
            "beiping-nanpi", "beiping-jinyang", "nanpi-pingyuan", "nanpi-ye", "jinyang-ye", "jinyang-luoyang", "jinyang-changan",
            "ye-pingyuan", "ye-puyang", "ye-luoyang", "pingyuan-beihai", "pingyuan-puyang", "beihai-xiapi",
            "puyang-chenliu", "puyang-xiaopei", "chenliu-luoyang", "chenliu-xuchang", "chenliu-xiaopei", "xiaopei-xiapi", "xiaopei-runan",
            "xiapi-guangling", "xiapi-shouchun", "xuchang-luoyang", "xuchang-wan", "xuchang-runan", "luoyang-changan", "luoyang-wan",
            "changan-tianshui", "changan-hanzhong", "tianshui-wuwei", "tianshui-hanzhong", "hanzhong-zitong", "zitong-chengdu",
            "chengdu-jiangzhou", "chengdu-jianning", "jiangzhou-yongan", "jiangzhou-jianning", "jianning-yunnan", "yongan-jiangling",
            "wan-xiangyang", "runan-shouchun", "runan-jiangxia", "shouchun-lujiang", "shouchun-guangling", "guangling-jianye",
            "lujiang-jianye", "lujiang-jiangxia", "lujiang-yuzhang", "jianye-wu", "wu-kuaiji", "kuaiji-yuzhang",
            "xiangyang-jiangling", "xiangyang-jiangxia", "jiangling-jiangxia", "jiangling-wuling", "jiangling-changsha",
            "jiangxia-changsha", "changsha-yuzhang", "changsha-guiyang", "changsha-lingling", "wuling-lingling", "lingling-guiyang", "guiyang-yuzhang",
        };

        // id | 势力名 | 君主 | 颜色 | 德 | 人望
        public static readonly string[] Factions =
        {
            "dong|董卓|董卓|#7a2a3a|10|70",
            "yuanshao|袁绍|袁绍|#2f6db5|55|85",
            "cao|曹操|曹操|#2c3d8f|60|80",
            "liubei|刘备|刘备|#2f9a55|95|60",
            "sunjian|孙坚|孙坚|#c8382c|70|72",
            "yuanshu|袁术|袁术|#c99a2e|25|65",
            "gongsun|公孙瓒|公孙瓒|#e0e0e8|50|62",
            "liubiao|刘表|刘表|#3aa5a0|70|65",
            "liuyan|刘焉|刘焉|#8a5fc0|55|55",
            "mateng|马腾|马腾|#c2702a|70|60",
            "taoqian|陶谦|陶谦|#6f8f3a|75|50",
            "kongrong|孔融|孔融|#b05a8a|80|55",
            "hanfu|韩馥|韩馥|#5a7a9a|50|40",
            "zhanglu|张鲁|张鲁|#a08a5a|60|45",
            "zhangyang|张杨|张杨|#7a6a8a|50|40",
            "liuyao|刘繇|刘繇|#4a8aa8|55|45",
            "lukang|陆康|陆康|#5aa070|70|40",
            "menghuo|孟获|孟获|#9a6a3a|40|50",
        };

        // 姓名 | 武力 | 智力 | 政治 | 势力 | 所在城 | 忠诚 | 兵力 | 在野隐士(1=需搜索)
        // 势力为 "-" 表示在野
        public static readonly string[] Generals =
        {
            // 董卓
            "董卓|86|64|30|dong|luoyang|100|6000|0", "吕布|100|26|13|dong|luoyang|70|5000|0", "李儒|20|90|80|dong|luoyang|95|1500|0",
            "华雄|90|48|30|dong|luoyang|85|4000|0", "李傕|76|42|20|dong|changan|85|3500|0", "郭汜|74|40|15|dong|changan|85|3500|0",
            "张济|72|56|42|dong|changan|80|3000|0", "樊稠|78|30|20|dong|changan|80|3000|0", "徐荣|78|68|50|dong|luoyang|85|3500|0",
            "张辽|94|78|58|dong|luoyang|65|4000|0", "高顺|86|60|44|dong|luoyang|80|3500|0", "贾诩|32|97|84|dong|changan|70|1500|0",
            "胡轸|70|36|30|dong|luoyang|80|2500|0", "牛辅|66|30|25|dong|changan|90|2500|0",
            // 袁绍
            "袁绍|70|76|74|yuanshao|nanpi|100|5000|0", "颜良|93|42|30|yuanshao|nanpi|90|4500|0", "文丑|94|28|22|yuanshao|nanpi|90|4500|0",
            "田丰|30|94|86|yuanshao|nanpi|80|1500|0", "沮授|40|92|84|yuanshao|nanpi|85|1500|0", "审配|58|80|72|yuanshao|nanpi|90|2500|0",
            "张郃|90|72|56|yuanshao|nanpi|75|4000|0", "高览|84|50|40|yuanshao|nanpi|80|3500|0", "逢纪|30|80|70|yuanshao|nanpi|85|1500|0",
            "郭图|28|78|66|yuanshao|nanpi|85|1500|0", "许攸|20|86|62|yuanshao|nanpi|65|1000|0", "麴义|86|52|40|yuanshao|nanpi|70|3500|0",
            // 曹操
            "曹操|72|94|92|cao|chenliu|100|5000|0", "夏侯惇|90|60|62|cao|chenliu|100|4500|0", "夏侯渊|91|58|52|cao|chenliu|100|4500|0",
            "曹仁|86|68|58|cao|chenliu|100|4000|0", "曹洪|80|46|42|cao|chenliu|100|3500|0", "乐进|86|52|40|cao|chenliu|95|3500|0",
            "李典|78|74|66|cao|chenliu|95|3000|0",
            // 刘备
            "刘备|76|76|78|liubei|pingyuan|100|3500|0", "关羽|98|78|62|liubei|pingyuan|100|4500|0", "张飞|99|34|26|liubei|pingyuan|100|4500|0",
            "简雍|30|72|74|liubei|pingyuan|95|1000|0",
            // 孙坚
            "孙坚|92|74|72|sunjian|changsha|100|5000|0", "孙策|96|72|70|sunjian|changsha|100|4500|0", "程普|82|74|66|sunjian|changsha|95|3500|0",
            "黄盖|84|66|56|sunjian|changsha|95|3500|0", "韩当|82|54|46|sunjian|changsha|95|3500|0", "祖茂|72|40|34|sunjian|changsha|95|2500|0",
            "朱治|70|66|70|sunjian|changsha|90|2500|0",
            // 袁术
            "袁术|60|62|56|yuanshu|wan|100|4500|0", "纪灵|86|46|38|yuanshu|wan|90|4000|0", "张勋|72|50|40|yuanshu|runan|85|3500|0",
            "桥蕤|70|42|36|yuanshu|runan|85|3000|0", "雷薄|68|30|24|yuanshu|wan|80|3000|0", "阎象|26|78|74|yuanshu|wan|80|1000|0",
            "杨弘|28|74|70|yuanshu|runan|85|1000|0",
            // 公孙瓒
            "公孙瓒|84|62|58|gongsun|beiping|100|5000|0", "赵云|97|78|70|gongsun|beiping|70|4000|0", "严纲|70|38|32|gongsun|beiping|90|3000|0",
            "田楷|62|50|48|gongsun|beiping|85|2500|0", "公孙越|66|40|36|gongsun|beiping|95|2500|0",
            // 刘表
            "刘表|50|74|80|liubiao|xiangyang|100|4000|0", "蔡瑁|66|70|62|liubiao|xiangyang|90|3500|0", "张允|58|50|48|liubiao|xiangyang|85|3000|0",
            "蒯良|26|86|82|liubiao|xiangyang|85|1000|0", "蒯越|30|88|80|liubiao|xiangyang|85|1000|0", "文聘|86|66|58|liubiao|jiangling|85|3500|0",
            "黄祖|72|54|44|liubiao|jiangxia|90|3500|0", "王威|64|50|44|liubiao|jiangling|85|2500|0",
            // 刘焉
            "刘焉|46|70|76|liuyan|chengdu|100|4000|0", "刘璋|30|44|56|liuyan|chengdu|100|2500|0", "张任|88|76|56|liuyan|chengdu|95|3500|0",
            "严颜|84|70|58|liuyan|jiangzhou|90|3500|0", "黄权|46|86|82|liuyan|chengdu|85|1500|0", "张松|18|88|80|liuyan|chengdu|65|1000|0",
            "吴懿|74|60|56|liuyan|zitong|85|3000|0", "冷苞|70|42|32|liuyan|zitong|85|2500|0",
            // 马腾
            "马腾|86|50|56|mateng|wuwei|100|4500|0", "马超|98|44|30|mateng|wuwei|95|4500|0", "韩遂|72|74|56|mateng|tianshui|75|4000|0",
            "庞德|94|66|44|mateng|wuwei|90|4000|0", "马岱|84|52|46|mateng|tianshui|95|3500|0", "成宜|66|32|24|mateng|tianshui|80|2500|0",
            // 陶谦
            "陶谦|40|58|72|taoqian|xiapi|100|3500|0", "糜竺|30|74|86|taoqian|xiapi|90|1500|0", "糜芳|56|38|40|taoqian|xiapi|80|2500|0",
            "曹豹|62|38|40|taoqian|xiaopei|80|3000|0", "陈登|52|86|84|taoqian|xiapi|75|2000|0", "臧霸|82|56|44|taoqian|xiaopei|70|3500|0",
            // 孔融
            "孔融|20|72|80|kongrong|beihai|100|2500|0", "武安国|78|26|20|kongrong|beihai|90|3000|0", "王修|36|70|78|kongrong|beihai|90|1000|0",
            // 韩馥
            "韩馥|40|52|62|hanfu|ye|100|3500|0", "潘凤|74|20|18|hanfu|ye|90|3500|0", "耿武|58|50|52|hanfu|ye|95|2500|0",
            "沮鹄|40|60|60|hanfu|ye|85|1500|0",
            // 张鲁
            "张鲁|50|72|76|zhanglu|hanzhong|100|3500|0", "阎圃|26|82|78|zhanglu|hanzhong|90|1000|0", "杨任|76|52|40|zhanglu|hanzhong|90|3000|0",
            "杨昂|72|40|30|zhanglu|hanzhong|85|3000|0", "张卫|70|40|34|zhanglu|hanzhong|95|3000|0",
            // 张杨
            "张杨|68|50|52|zhangyang|jinyang|100|3500|0", "眭固|66|30|24|zhangyang|jinyang|80|3000|0", "杨丑|60|34|30|zhangyang|jinyang|75|2500|0",
            // 刘繇
            "刘繇|50|64|70|liuyao|jianye|100|3500|0", "张英|68|40|34|liuyao|jianye|85|3000|0", "笮融|56|52|30|liuyao|wu|70|2500|0",
            "樊能|64|30|24|liuyao|jianye|85|2500|0", "薛礼|58|46|40|liuyao|wu|80|2500|0",
            // 陆康
            "陆康|44|72|76|lukang|lujiang|100|3000|0", "陆绩|20|76|74|lukang|lujiang|95|1000|0",
            // 孟获
            "孟获|88|40|44|menghuo|jianning|100|4500|0", "祝融|86|38|30|menghuo|jianning|100|3500|0", "孟优|74|30|24|menghuo|yunnan|100|3000|0",
            "兀突骨|92|10|8|menghuo|yunnan|90|4000|0", "带来洞主|70|36|24|menghuo|jianning|90|2500|0",
            // 在野隐士（须「搜索」方能发现）
            "诸葛亮|38|100|96|-|xiangyang|0|0|1", "庞统|34|97|86|-|xiangyang|0|0|1", "徐庶|64|92|84|-|xuchang|0|0|1",
            "荀彧|20|96|98|-|xuchang|0|0|1", "郭嘉|20|98|82|-|xuchang|0|0|1", "荀攸|30|94|86|-|xuchang|0|0|1",
            "程昱|46|90|82|-|puyang|0|0|1", "典韦|96|36|20|-|chenliu|0|0|1", "许褚|96|34|20|-|runan|0|0|1",
            "于禁|78|70|60|-|puyang|0|0|1", "满宠|56|82|78|-|xuchang|0|0|1", "徐晃|91|70|54|-|luoyang|0|0|1",
            "司马懿|62|99|94|-|luoyang|0|0|1", "钟繇|20|76|90|-|changan|0|0|1", "姜维|90|90|72|-|tianshui|0|0|1",
            "周瑜|78|97|86|-|lujiang|0|0|1", "鲁肃|58|92|90|-|guangling|0|0|1", "张昭|20|86|96|-|guangling|0|0|1",
            "吕蒙|84|88|76|-|shouchun|0|0|1", "周泰|90|50|36|-|shouchun|0|0|1", "蒋钦|82|52|44|-|shouchun|0|0|1",
            "甘宁|94|74|38|-|jiangzhou|0|0|1", "太史慈|94|68|52|-|beihai|0|0|1", "陆逊|70|96|90|-|wu|0|0|1",
            "凌统|86|50|40|-|wu|0|0|1", "黄忠|94|66|52|-|changsha|0|0|1", "魏延|92|68|44|-|xiangyang|0|0|1",
            "马良|28|88|86|-|jiangling|0|0|1", "马谡|62|84|70|-|jiangling|0|0|1", "法正|46|94|80|-|changan|0|0|1",
            "孟达|74|64|58|-|changan|0|0|1", "王平|76|70|60|-|hanzhong|0|0|1", "邓艾|86|92|80|-|wan|0|0|1",
            "孙乾|30|70|80|-|beihai|0|0|1", "关平|80|60|54|-|pingyuan|0|0|1", "周仓|84|34|20|-|runan|0|0|1",
            "张绣|84|56|44|-|wan|0|0|1", "华佗|10|80|60|-|xiaopei|0|0|1", "鲁宗|60|40|40|-|yuzhang|0|0|1",
            "蒋琬|30|84|92|-|lingling|0|0|1", "刘巴|20|80|88|-|lingling|0|0|1", "霍峻|76|66|58|-|jiangling|0|0|1",
            "赵范|50|46|52|-|guiyang|0|0|1", "金旋|56|40|40|-|wuling|0|0|1", "刘度|40|44|56|-|lingling|0|0|1",
            "华歆|20|76|84|-|yuzhang|0|0|1", "王朗|30|76|80|-|kuaiji|0|0|1", "虞翻|40|84|78|-|kuaiji|0|0|1",
            "严白虎|70|36|30|-|wu|0|0|1", "张燕|80|56|40|-|jinyang|0|0|1", "沙摩柯|90|20|10|-|wuling|0|0|1",
        };
    }
}
