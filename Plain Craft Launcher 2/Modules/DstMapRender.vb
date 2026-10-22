''' <summary>
''' Minecraft 存档地图渲染引擎。
''' 直接读 .mca 区域文件里的区块 NBT，取出最上层非空气方块，按方块调色板着色。
''' 全部基于采样：一格地图像素 = 1 个方块。
''' </summary>
Public Module DstMapRender

    ''' <summary>一个待渲染的区块任务。</summary>
    Public Class ChunkTask
        ''' <summary>区块 X（世界坐标除以 16）。</summary>
        Public ChunkX As Integer
        ''' <summary>区块 Z。</summary>
        Public ChunkZ As Integer
        ''' <summary>区域文件路径。</summary>
        Public McaPath As String = ""
        ''' <summary>区域文件里的位置下标（0..1023）。</summary>
        Public Slot As Integer = -1
    End Class

    ''' <summary>方块颜色表。键是不带命名空间的方块名。</summary>
    Private ReadOnly Colors As New Dictionary(Of String, Integer)

    ''' <summary>
    ''' 用户自定义的方块颜色（不带命名空间的方块名 → RGB）。
    ''' 存在启动器设置里，优先级高于内置颜色表。
    ''' </summary>
    Private ReadOnly UserColors As New Dictionary(Of String, Integer)
    ''' <summary>自定义颜色是否已从设置里载入。</summary>
    Private UserColorsLoaded As Boolean = False
    Private Const SettingKey As String = "DstMapBlockColors"

    ''' <summary>初始化调色板。土壤类按 MC 自己的材质取接近的均值，混凝土与羊毛用官方 16 色。</summary>
    Private Sub InitColors()
        If Colors.Count > 0 Then Return
        '地形
        AddColor("stone", &H7F7F7F)
        AddColor("cobblestone", &H7A7A7A)
        AddColor("granite", &H9A6B5B)
        AddColor("diorite", &HC3C3C3)
        AddColor("andesite", &H8B8B8B)
        AddColor("deepslate", &H4F4F52)
        AddColor("tuff", &H6D6D65)
        AddColor("calcite", &HDDDDD6)
        AddColor("dirt", &H866043)
        AddColor("coarse_dirt", &H77563A)
        AddColor("rooted_dirt", &H91694E)
        AddColor("mud", &H3C3330)
        AddColor("clay", &HA0A6B5)
        AddColor("gravel", &H877F7D)
        AddColor("sand", &HDBCFA3)
        AddColor("red_sand", &HB9631F)
        AddColor("sandstone", &HD8CB9B)
        AddColor("red_sandstone", &HB4611C)
        AddColor("grass_block", &H7CBD5B)
        AddColor("grass", &H7CBD5B)
        AddColor("podzol", &H5B3F26)
        AddColor("mycelium", &H6F6265)
        AddColor("dirt_path", &H9B7F4B)
        AddColor("farmland", &H6B4A30)
        AddColor("snow_block", &HF2FBFB)
        AddColor("snow", &HF2FBFB)
        AddColor("ice", &HA0C4F0)
        AddColor("packed_ice", &H92B2EE)
        AddColor("blue_ice", &H74A2F2)
        AddColor("frosted_ice", &H9CC3EF)
        AddColor("water", &H2A5FD8)
        AddColor("lava", &HEB6C18)
        AddColor("bedrock", &H565656)
        AddColor("obsidian", &H150C1D)
        AddColor("netherrack", &H6F3436)
        AddColor("soul_sand", &H4F3D33)
        AddColor("soul_soil", &H4B3A2E)
        AddColor("magma_block", &H8E3D1E)
        AddColor("glowstone", &HCC9966)
        AddColor("end_stone", &HD9DBA8)
        AddColor("end_stone_bricks", &HD2D4A0)
        AddColor("purpur_block", &HA97BA7)
        AddColor("moss_block", &H5C7B33)
        AddColor("moss_carpet", &H5C7B33)
        AddColor("sculk", &H0E2127)
        AddColor("sculk_catalyst", &H12282E)
        AddColor("amethyst_block", &H8861C6)
        '树木与植被
        AddColor("oak_leaves", &H48822B)
        AddColor("spruce_leaves", &H3A5627)
        AddColor("birch_leaves", &H7A8A4B)
        AddColor("jungle_leaves", &H32561A)
        AddColor("acacia_leaves", &H6C7A2A)
        AddColor("dark_oak_leaves", &H305718)
        AddColor("mangrove_leaves", &H4A6B2A)
        AddColor("cherry_leaves", &HE7A7C6)
        AddColor("azalea_leaves", &H5A7038)
        AddColor("flowering_azalea_leaves", &H8B6E85)
        AddColor("pale_oak_leaves", &H9CA48C)
        AddColor("oak_log", &H6B5334)
        AddColor("spruce_log", &H3A281C)
        AddColor("birch_log", &HD6CFC0)
        AddColor("jungle_log", &H56421F)
        AddColor("acacia_log", &H67634B)
        AddColor("dark_oak_log", &H3C2C18)
        AddColor("cherry_log", &HD9B1B1)
        AddColor("oak_planks", &HB08F56)
        AddColor("spruce_planks", &H745631)
        AddColor("birch_planks", &HD7C185)
        AddColor("jungle_planks", &HA87A54)
        AddColor("acacia_planks", &HB05D33)
        AddColor("dark_oak_planks", &H4A3218)
        AddColor("bamboo_block", &HB4A94E)
        AddColor("cactus", &H0F6A1A)
        AddColor("sugar_cane", &H96C578)
        AddColor("pumpkin", &HD77F18)
        AddColor("carved_pumpkin", &HD77F18)
        AddColor("jack_o_lantern", &HD9902A)
        AddColor("melon", &H6F9F2D)
        AddColor("nether_wart_block", &H720202)
        AddColor("warped_wart_block", &H167E88)
        AddColor("shroomlight", &HF09A4E)
        AddColor("crimson_nylium", &H8B0A1B)
        AddColor("warped_nylium", &H2E6C63)
        '人造方块
        AddColor("bricks", &H96604F)
        AddColor("stone_bricks", &H7A7A7A)
        AddColor("deepslate_bricks", &H4B4B4E)
        AddColor("deepslate_tiles", &H37373A)
        AddColor("mossy_stone_bricks", &H6D7A62)
        AddColor("nether_bricks", &H2D161C)
        AddColor("end_stone_bricks", &HD2D4A0)
        AddColor("quartz_block", &HEAE7E1)
        AddColor("smooth_quartz", &HEAE7E1)
        AddColor("prismarine", &H6BA39C)
        AddColor("prismarine_bricks", &H69B1AA)
        AddColor("dark_prismarine", &H366152)
        AddColor("sea_lantern", &HACCBCB)
        AddColor("glass", &HA8C6D8)
        AddColor("glass_pane", &HA8C6D8)
        AddColor("tinted_glass", &H38333D)
        AddColor("iron_block", &HD8D8D8)
        AddColor("gold_block", &HF6D438)
        AddColor("diamond_block", &H63E2DB)
        AddColor("emerald_block", &H2FDD64)
        AddColor("lapis_block", &H1D4A9E)
        AddColor("redstone_block", &HAF1B09)
        AddColor("netherite_block", &H443F42)
        AddColor("copper_block", &HC0694E)
        AddColor("exposed_copper", &HA3846E)
        AddColor("weathered_copper", &H6D8A71)
        AddColor("oxidized_copper", &H4F9077)
        AddColor("coal_block", &H101010)
        AddColor("raw_iron_block", &HD5A67E)
        AddColor("raw_copper_block", &H9E6A4F)
        AddColor("raw_gold_block", &HE0B940)
        AddColor("amethyst_block", &H8861C6)
        AddColor("honey_block", &HF7A521)
        AddColor("slime_block", &H77C660)
        '混凝土 —— 16 色，取 MC 的官方色值
        ConcreteColors("white", &HCFD5D1)
        ConcreteColors("orange", &HE06101)
        ConcreteColors("magenta", &HA9309B)
        ConcreteColors("light_blue", &H2389C9)
        ConcreteColors("yellow", &HF1AF16)
        ConcreteColors("lime", &H5EA919)
        ConcreteColors("pink", &HD5658E)
        ConcreteColors("gray", &H36393D)
        ConcreteColors("light_gray", &H7D7D73)
        ConcreteColors("cyan", &H157888)
        ConcreteColors("purple", &H64209C)
        ConcreteColors("blue", &H2C2E8F)
        ConcreteColors("brown", &H603B1F)
        ConcreteColors("green", &H495B24)
        ConcreteColors("red", &H8E2121)
        ConcreteColors("black", &H080A0E)
        '羊毛 —— 16 色
        WoolColors("white", &HE9ECEC)
        WoolColors("orange", &HF07613)
        WoolColors("magenta", &HBD44B3)
        WoolColors("light_blue", &H3AAFD9)
        WoolColors("yellow", &HF8C627)
        WoolColors("lime", &H70B919)
        WoolColors("pink", &HED8DAC)
        WoolColors("gray", &H3E4447)
        WoolColors("light_gray", &H8E8E86)
        WoolColors("cyan", &H158991)
        WoolColors("purple", &H792AAF)
        WoolColors("blue", &H35399D)
        WoolColors("brown", &H724728)
        WoolColors("green", &H546D1A)
        WoolColors("red", &HA12722)
        WoolColors("black", &H141519)
        '带颜色的方块：混凝土粉末、陶瓦、玻璃、地毯、床等，按同色系近似
        PowderColors("white", &HE1DDD8)
        PowderColors("orange", &HE38421)
        PowderColors("magenta", &HC2569C)
        PowderColors("light_blue", &H4EBBE2)
        PowderColors("yellow", &HE9C242)
        PowderColors("lime", &H8AC23E)
        PowderColors("pink", &HF09FB1)
        PowderColors("gray", &H4C5153)
        PowderColors("light_gray", &H9C9A92)
        PowderColors("cyan", &H278E97)
        PowderColors("purple", &H8A45AF)
        PowderColors("blue", &H454BA3)
        PowderColors("brown", &H8A5A38)
        PowderColors("green", &H657D22)
        PowderColors("red", &HB02E28)
        PowderColors("black", &H1B1C20)
        TerracottaColors("white", &HD1B2A1)
        TerracottaColors("orange", &HA15325)
        TerracottaColors("magenta", &H95576B)
        TerracottaColors("light_blue", &H706C8A)
        TerracottaColors("yellow", &HBA8523)
        TerracottaColors("lime", &H677534)
        TerracottaColors("pink", &HA14E4E)
        TerracottaColors("gray", &H392A23)
        TerracottaColors("light_gray", &H876A5C)
        TerracottaColors("cyan", &H575B5B)
        TerracottaColors("purple", &H764656)
        TerracottaColors("blue", &H4A3B5B)
        TerracottaColors("brown", &H4D3323)
        TerracottaColors("green", &H4C532A)
        TerracottaColors("red", &H8E3C27)
        TerracottaColors("black", &H251A16)
        TerracottaColors("", &H985E43)
        GlassColors("white", &HF2F4F4)
        GlassColors("orange", &HF7B169)
        GlassColors("magenta", &HDC9EDA)
        GlassColors("light_blue", &HA9D5F1)
        GlassColors("yellow", &HF8EA9D)
        GlassColors("lime", &HE1F3A7)
        GlassColors("pink", &HDDB7D0)
        GlassColors("gray", &H989EA0)
        GlassColors("light_gray", &HD4D8D9)
        GlassColors("cyan", &H95C1DE)
        GlassColors("purple", &HD3A1E8)
        GlassColors("blue", &H9EA9E3)
        GlassColors("brown", &HBF906F)
        GlassColors("green", &HAECB87)
        GlassColors("red", &HE3918F)
        GlassColors("black", &H616569)
        '常见单色石头类
        AddColor("smooth_stone", &H9F9F9F)
        AddColor("polished_andesite", &H848484)
        AddColor("polished_diorite", &HBDBDBD)
        AddColor("polished_granite", &H9A6457)
        AddColor("polished_deepslate", &H4A4A4D)
        AddColor("netherrack", &H6F3436)
        AddColor("basalt", &H4D4D52)
        AddColor("blackstone", &H2B2630)
        AddColor("smooth_basalt", &H48484D)
        AddColor("dripstone_block", &H866C5A)
        AddColor("pointed_dripstone", &H866C5A)
        AddColor("ochre_froglight", &HF9E6A0)
        AddColor("verdant_froglight", &HE0EFB5)
        AddColor("pearlescent_froglight", &HE9CFDF)
        AddColor("mushroom_stem", &HCDB9A2)
        AddColor("brown_mushroom_block", &HAE7B54)
        AddColor("red_mushroom_block", &HC63C26)
        AddColor("hay_block", &HE0B421)
        AddColor("crafting_table", &H9E6A4A)
        AddColor("furnace", &H7A7A7A)
        AddColor("chest", &H9C7F3C)
        AddColor("barrel", &H8E6C3E)
        AddColor("bookshelf", &H9C7F3C)
        AddColor("tnt", &HDB3A28)
        '矿石（地表可见的那些）
        AddColor("coal_ore", &H6B6B6B)
        AddColor("iron_ore", &H8C7B72)
        AddColor("copper_ore", &H8A7C6B)
        AddColor("gold_ore", &H9C9060)
        AddColor("lapis_ore", &H4A5B8C)
        AddColor("redstone_ore", &H8C5B5B)
        AddColor("diamond_ore", &H6E8E8E)
        AddColor("emerald_ore", &H5E8C5E)
        AddColor("deepslate_iron_ore", &H4A4A4E)
        AddColor("deepslate_copper_ore", &H4A4A4E)
        AddColor("deepslate_gold_ore", &H4A4A4E)
        AddColor("deepslate_lapis_ore", &H44444E)
        AddColor("deepslate_redstone_ore", &H4A4044)
        AddColor("deepslate_diamond_ore", &H3E4A4E)
        AddColor("deepslate_emerald_ore", &H3E4A44)
        AddColor("deepslate_coal_ore", &H46464A)
        AddColor("tuff_bricks", &H63635D)
        AddColor("chiseled_tuff_bricks", &H5A5A55)
        AddColor("powder_snow", &HF0F5FA)
        AddColor("short_grass", &H7CBD5B)
        AddColor("tall_grass", &H7CBD5B)
        AddColor("fern", &H6A9E4B)
        AddColor("waxed_copper_block", &HC0694E)
        AddColor("waxed_exposed_copper", &HA3846E)
        AddColor("waxed_weathered_copper", &H6D8A71)
        AddColor("waxed_oxidized_copper", &H4F9077)
        '常见模组方块（Create 等），认出来总比一律灰色好
        AddColor("asurine", &H3E7A8C)
        AddColor("veridium", &H3F7A5E)
        AddColor("limestone", &HA8A48F)
        AddColor("zinc_ore", &H8A9490)
        AddColor("deepslate_zinc_ore", &H4A4A4E)
        AddColor("crimsite", &H8C4A42)
        AddColor("ochrum", &HB8934A)
        '26.x 新方块
        AddColor("leaf_litter", &H9A6B3A)
        AddColor("bush", &H4F7A2E)
        AddColor("wildflowers", &H7FB04A)
        AddColor("firefly_bush", &H66803A)
        AddColor("short_dry_grass", &HC4A96A)
        AddColor("tall_dry_grass", &HC4A96A)
        AddColor("pale_moss_block", &H8E9B84)
        AddColor("pale_moss_carpet", &H8E9B84)
        AddColor("resin_block", &HDA7530)
        AddColor("resin_clump", &HDA7530)
        AddColor("creaking_heart", &H6A4A2A)
        AddColor("copper_grate", &HC0694E)
        AddColor("waxed_copper_grate", &HC0694E)
        AddColor("cactus_flower", &HE05A8A)
    End Sub

    Private Sub AddColor(Name As String, Rgb As Integer)
        If Not Colors.ContainsKey(Name) Then Colors(Name) = Rgb
    End Sub

    Private Sub ConcreteColors(Color As String, Rgb As Integer)
        AddColor(If(Color = "", "white_concrete", Color & "_concrete"), Rgb)
    End Sub

    Private Sub WoolColors(Color As String, Rgb As Integer)
        AddColor(If(Color = "", "white_wool", Color & "_wool"), Rgb)
    End Sub

    Private Sub PowderColors(Color As String, Rgb As Integer)
        AddColor(If(Color = "", "white_concrete_powder", Color & "_concrete_powder"), Rgb)
    End Sub

    Private Sub TerracottaColors(Color As String, Rgb As Integer)
        AddColor(If(Color = "", "terracotta", Color & "_terracotta"), Rgb)
    End Sub

    Private Sub GlassColors(Color As String, Rgb As Integer)
        AddColor(If(Color = "", "white_stained_glass", Color & "_stained_glass"), Rgb)
    End Sub

#Region "自定义颜色"

    ''' <summary>
    ''' 从启动器设置里载入用户自定义颜色。
    ''' 存的格式：方块名=RRGGBB，多个用逗号分隔。
    ''' </summary>
    Public Sub LoadUserColors()
        If UserColorsLoaded Then Return
        UserColorsLoaded = True
        UserColors.Clear()
        Try
            Dim Raw As String = Settings.Get(Of String)(SettingKey)
            If String.IsNullOrWhiteSpace(Raw) Then Return
            For Each Pair In Raw.Split(","c)
                Dim Idx As Integer = Pair.IndexOf("="c)
                If Idx <= 0 OrElse Idx >= Pair.Length - 1 Then Continue For
                Dim Name As String = Pair.Substring(0, Idx).Trim()
                Dim HexText As String = Pair.Substring(Idx + 1).Trim()
                If Name = "" OrElse HexText.Length <> 6 Then Continue For
                Dim V As Integer
                If Integer.TryParse(HexText, Globalization.NumberStyles.HexNumber, Nothing, V) Then
                    UserColors(Name) = V
                End If
            Next
        Catch ex As Exception
            Logger.Warn(ex, "载入自定义方块颜色失败")
        End Try
    End Sub

    ''' <summary>把用户自定义颜色写回设置。</summary>
    Private Sub SaveUserColors()
        Try
            Dim Parts As New List(Of String)
            For Each Pair In UserColors
                Parts.Add(Pair.Key & "=" & Pair.Value.ToString("X6"))
            Next
            Settings.Set(SettingKey, String.Join(",", Parts.ToArray()))
        Catch ex As Exception
            Logger.Warn(ex, "保存自定义方块颜色失败")
        End Try
    End Sub

    ''' <summary>某个方块是否有自定义颜色。</summary>
    Public Function HasUserColor(BlockId As String) As Boolean
        LoadUserColors()
        Dim Name As String = DstNbt.StripNamespace(BlockId)
        Return UserColors.ContainsKey(Name)
    End Function

    ''' <summary>取某个方块的自定义颜色，没有返回 Nothing。</summary>
    Public Function GetUserColor(BlockId As String) As Integer?
        LoadUserColors()
        Dim Name As String = DstNbt.StripNamespace(BlockId)
        Dim Rgb As Integer
        If UserColors.TryGetValue(Name, Rgb) Then Return Rgb
        Return Nothing
    End Function

    ''' <summary>设置某个方块的自定义颜色。传 Nothing 则恢复默认。</summary>
    Public Sub SetUserColor(BlockId As String, Rgb As Integer?)
        LoadUserColors()
        Dim Name As String = DstNbt.StripNamespace(BlockId)
        If Name = "" Then Return
        If Rgb.HasValue Then
            UserColors(Name) = Rgb.Value
        Else
            UserColors.Remove(Name)
        End If
        SaveUserColors()
    End Sub

    ''' <summary>清空所有自定义颜色。</summary>
    Public Sub ClearUserColors()
        LoadUserColors()
        UserColors.Clear()
        SaveUserColors()
    End Sub

    ''' <summary>有多少个自定义颜色。</summary>
    Public Function UserColorCount() As Integer
        LoadUserColors()
        Return UserColors.Count
    End Function

#End Region

    ''' <summary>查方块颜色。不认识的方块按名字哈希给一个稳定的灰调，避免地图上一片死黑。</summary>
    Public Function ColorOf(BlockId As String) As Integer
        InitColors()
        LoadUserColors()
        If BlockId Is Nothing Then Return &H808080
        Dim Name As String = DstNbt.StripNamespace(BlockId)
        '用户自定义颜色优先
        Dim Override As Integer
        If UserColors.TryGetValue(Name, Override) Then Return Override
        Dim Rgb As Integer
        If Colors.TryGetValue(Name, Rgb) Then Return Rgb
        '一些方块有变体后缀，比如 oak_stairs / white_carpet，先做后缀剥离再查
        For Each Suffix In {"_stairs", "_slab", "_wall", "_fence", "_fence_gate", "_door", "_trapdoor",
                            "_button", "_pressure_plate", "_carpet", "_banner", "_sign", "_wall_sign", "_hanging_sign"}
            If Name.EndsWith(Suffix, StringComparison.Ordinal) Then
                Dim Base As String = Name.Substring(0, Name.Length - Suffix.Length)
                If Colors.TryGetValue(Base, Rgb) Then Return Rgb
                If Suffix = "_carpet" AndAlso Colors.TryGetValue(Base & "_wool", Rgb) Then Return Rgb
            End If
        Next
        '树干类的变体：xxx_wood / stripped_xxx_log
        If Name.EndsWith("_wood") Then
            Dim Base2 As String = Name.Substring(0, Name.Length - 5) & "_log"
            If Colors.TryGetValue(Base2, Rgb) Then Return Rgb
        End If
        If Name.StartsWith("stripped_") AndAlso Colors.TryGetValue(Name.Substring(9), Rgb) Then Return Rgb
        '稳定哈希出一个中性色，别让它显得像空洞。
        '必须用 Long 算：H * 31 在 Integer 里会溢出，开了溢出检查就会直接抛异常。
        Dim H As Long = 0
        For Each Ch In Name
            H = (H * 31 + AscW(Ch)) And &H7FFFFFFFL
        Next
        Dim Gray As Integer = 96 + CInt(H Mod 64)
        Return (Gray << 16) Or (Gray << 8) Or Gray
    End Function

    ''' <summary>
    ''' 从区块的方块状态名称解析出它属于哪种「方块」。
    ''' MC 1.13+ 的 palette 里存的是 minecraft:grass_block[snowy=false] 这种带属性串的形式。
    ''' </summary>
    Private Function CleanBlockId(Raw As String) As String
        If Raw Is Nothing Then Return ""
        Dim Bracket As Integer = Raw.IndexOf("["c)
        If Bracket > 0 Then Raw = Raw.Substring(0, Bracket)
        Return Raw
    End Function

    ''' <summary>天然就该忽略的方块，它们不该出现在地表判断里。</summary>
    Private Function IsTransparent(Id As String) As Boolean
        Dim Name As String = DstNbt.StripNamespace(Id)
        Select Case Name
            Case "air", "cave_air", "void_air", "structure_void", "barrier", "light", "moving_piston"
                Return True
        End Select
        '树叶、玻璃、水这类不该被当成地表，但要特殊处理，这里不算「完全空气」
        Return False
    End Function

    ''' <summary>
    ''' 草、花、海草、栅栏这类非实心装饰。俯视图上它们盖住的是地面而不是自己，
    ''' 所以要跳过，露出下面的真实地表。不跳的话整片海底会被海草染绿、地面会被草丛打花。
    ''' </summary>
    Private ReadOnly DecorNames As New HashSet(Of String) From {
        "fern", "short_grass", "tall_grass", "large_fern", "dead_bush", "sweet_berry_bush",
        "seagrass", "tall_seagrass", "kelp", "kelp_plant", "bubble_column",
        "poppy", "dandelion", "blue_orchid", "allium", "azure_bluet", "red_tulip", "orange_tulip",
        "white_tulip", "pink_tulip", "oxeye_daisy", "cornflower", "lily_of_the_valley", "wither_rose",
        "sunflower", "lilac", "rose_bush", "peony", "torchflower", "pitcher_plant",
        "brown_mushroom", "red_mushroom", "crimson_fungus", "warped_fungus", "crimson_roots",
        "warped_roots", "nether_sprouts", "glow_lichen", "sculk_vein", "hanging_roots",
        "big_dripleaf", "small_dripleaf", "rail", "powered_rail", "detector_rail", "activator_rail",
        "oak_fence", "spruce_fence", "birch_fence", "jungle_fence", "acacia_fence",
        "dark_oak_fence", "mangrove_fence", "cherry_fence", "bamboo_fence", "nether_brick_fence",
        "crimson_fence", "warped_fence", "oak_sign", "spruce_sign", "birch_sign",
        "torch", "wall_torch", "soul_torch", "lantern", "snow", "vine", "ladder", "cobweb",
        "lily_pad", "sugar_cane", "bamboo", "cactus_flower", "carpet"
    }

    Private Function IsDecor(Id As String) As Boolean
        Dim Name As String = DstNbt.StripNamespace(Id)
        If DecorNames.Contains(Name) Then Return True
        '26.x 新增的贴地装饰，不跳过就会盖住地面形成斑点
        Select Case Name
            Case "leaf_litter", "bush", "wildflowers", "firefly_bush", "short_dry_grass",
                 "tall_dry_grass", "cactus_flower", "pale_moss_carpet", "resin_clump",
                 "glow_lichen", "sculk_vein", "amethyst_cluster", "large_amethyst_bud",
                 "medium_amethyst_bud", "small_amethyst_bud", "pointed_dripstone",
                 "cave_vines", "cave_vines_plant", "weeping_vines", "weeping_vines_plant",
                 "twisting_vines", "twisting_vines_plant", "spore_blossom",
                 "dead_fire_coral", "dead_tube_coral", "dead_brain_coral",
                 "dead_bubble_coral", "dead_horn_coral",
                 "cake", "white_carpet", "nether_wart", "cocoa", "sea_pickle",
                 "sweet_berry_bush", "brown_mushroom", "red_mushroom"
                Return True
        End Select
        '任何 *_carpet / *_vein / *_litter 都是贴地装饰
        If Name.EndsWith("_carpet", StringComparison.Ordinal) Then Return True
        If Name.EndsWith("_vein", StringComparison.Ordinal) Then Return True
        If Name.EndsWith("_litter", StringComparison.Ordinal) Then Return True
        If Name.EndsWith("_cluster", StringComparison.Ordinal) Then Return True
        If Name.EndsWith("_bud", StringComparison.Ordinal) Then Return True
        Return False
    End Function

    ''' <summary>流体：水面就是地表，需要按水的颜色画。</summary>
    Private Function IsFluid(Id As String) As Boolean
        Dim Name As String = DstNbt.StripNamespace(Id)
        Return Name = "water" OrElse Name = "lava" OrElse Name = "flowing_water" OrElse Name = "flowing_lava"
    End Function

    ''' <summary>是不是水（不包含岩浆）。水面要按半透明画，透出下面的方块。</summary>
    Public Function IsWater(Id As String) As Boolean
        Dim Name As String = DstNbt.StripNamespace(Id)
        Return Name = "water" OrElse Name = "flowing_water"
    End Function

    ''' <summary>
    ''' 从 palette 的一个元素里取出方块 ID。
    ''' 该字段在不同版本里出现过四种写法，都要认：
    '''   1. 旧版：compound { Name: "minecraft:stone", Properties: {...} }
    '''   2. 26.1+：纯字符串 "minecraft:stone"
    '''   3. 26.3 过渡：单键 compound { "": "minecraft:stone" }
    '''   4. 26.3 正式：compound { id: "minecraft:stone", properties: {...} }
    ''' 任何一条没认出来都会让整列被当成空气，地图上就是一块斑点。
    ''' </summary>
    Private Function PaletteNameOf(Item As NbtTag) As String
        If Item Is Nothing Then Return ""
        '1) 纯字符串
        If Item.TagType = DstNbt.TypeString Then
            Return CleanBlockId(DirectCast(Item.TagValue, String))
        End If
        If Item.TagType <> DstNbt.TypeCompound Then Return ""
        '2) Name 字段（旧版）
        Dim NameTag As NbtTag = DstNbt.CompoundGet(Item, "Name")
        If NameTag IsNot Nothing AndAlso NameTag.TagType = DstNbt.TypeString Then
            Return CleanBlockId(DirectCast(NameTag.TagValue, String))
        End If
        '3) id 字段（26.3 正式）
        Dim IdTag As NbtTag = DstNbt.CompoundGet(Item, "id")
        If IdTag IsNot Nothing AndAlso IdTag.TagType = DstNbt.TypeString Then
            Return CleanBlockId(DirectCast(IdTag.TagValue, String))
        End If
        '4) 单键 compound：键名是空串，值才是方块 ID（26.3 过渡）
        Dim Kids As List(Of NbtTag) = DstNbt.Children(Item)
        If Kids.Count = 1 AndAlso Kids(0).TagType = DstNbt.TypeString Then
            Return CleanBlockId(DirectCast(Kids(0).TagValue, String))
        End If
        Return ""
    End Function

    ''' <summary>
    ''' 从区块 NBT 里取出每个方块列的地表与水下底层。
    ''' Surface 按 z*16+x 排列，是这一列最上层可见方块（可能是水）。
    ''' Under 是同一列里地表下方的第一个不透明方块，用于把水面画成半透明后透出海底。
    ''' </summary>
    Public Function ReadChunkSurface(ChunkRoot As NbtTag, ByRef Under As String(), ByRef Heights As Short()) As String()
        Dim Result(255) As String
        Under = New String(255) {}
        Heights = New Short(255) {}
        For i = 0 To 255
            Result(i) = ""
            Under(i) = ""
            Heights(i) = -999
        Next
        If ChunkRoot Is Nothing Then Return Result
        Dim Sections As NbtTag = DstNbt.CompoundGet(ChunkRoot, "sections")
        If Sections Is Nothing Then Sections = DstNbt.CompoundGet(ChunkRoot, "Level.sections")
        If Sections Is Nothing Then Sections = DstNbt.GetByPath(ChunkRoot, "Level.Sections")
        If Sections Is Nothing Then Return Result

        Dim SectionList As New List(Of NbtTag)
        For Each Section In DstNbt.Children(Sections)
            If Section.TagType = DstNbt.TypeCompound Then SectionList.Add(Section)
        Next
        '按 Y 从高到低处理，第一个碰到的不透明方块就是地表
        SectionList.Sort(
            Function(A, B)
                Return SectionY(B).CompareTo(SectionY(A))
            End Function)

        Dim Filled(255) As Boolean
        Dim HasFluid(255) As Boolean
        For Each Section In SectionList
            ParseSection(Section, Result, Under, Heights, Filled, HasFluid)
            Dim AllDone As Boolean = True
            For i = 0 To 255
                If Not Filled(i) Then
                    AllDone = False
                    Exit For
                End If
            Next
            If AllDone Then Exit For
        Next
        Return Result
    End Function
    Private Function SectionY(Section As NbtTag) As Integer
        Dim Tag As NbtTag = DstNbt.CompoundGet(Section, "Y")
        If Tag Is Nothing Then Return 0
        Return CInt(Tag.TagValue)
    End Function

    ''' <summary>
    ''' 解析一个 section（16×16×16）里每一列最上层的方块。
    ''' 碰到流体时先记下水面，继续往下找第一个不透明方块记进 Under。
    ''' Filled 表示「地表与水下都已确定」，HasFluid 表示「地表是水、还在找海底」。
    ''' </summary>
    Private Sub ParseSection(Section As NbtTag, ByRef Result As String(), ByRef Under As String(),
                             ByRef Heights As Short(), ByRef Filled As Boolean(), ByRef HasFluid As Boolean())
        Dim SecY As Integer = SectionY(Section)
        Dim BlockStates As NbtTag = DstNbt.CompoundGet(Section, "block_states")
        If BlockStates Is Nothing Then BlockStates = DstNbt.CompoundGet(Section, "block_states")
        Dim PaletteTag As NbtTag = Nothing
        Dim DataTag As NbtTag = Nothing
        If BlockStates IsNot Nothing Then
            PaletteTag = DstNbt.CompoundGet(BlockStates, "palette")
            DataTag = DstNbt.CompoundGet(BlockStates, "data")
        Else
            '1.18 之前是 1.16 的 Palette + BlockStates
            PaletteTag = DstNbt.CompoundGet(Section, "Palette")
            DataTag = DstNbt.CompoundGet(Section, "BlockStates")
        End If
        If PaletteTag Is Nothing Then Return
        Dim Palette As New List(Of String)
        For Each Item In DstNbt.Children(PaletteTag)
            Palette.Add(PaletteNameOf(Item))
        Next
        If Palette.Count = 0 Then Return
        '每格占多少位：MC 的规则是 max(4, 能放下 0..n-1 的最少位数)
        '这里必须用整数算，Math.Log(32, 2) 有浮点误差会多算一位，导致整块解错
        Dim Bits As Integer = 4
        Dim Needed As Integer = Palette.Count - 1
        While Needed > (1 << Bits) - 1
            Bits += 1
        End While
        '空 section 只有一层调色板，全部同一个方块
        If DataTag Is Nothing OrElse Palette.Count = 1 Then
            Dim Id As String = If(Palette.Count = 0, "", Palette(0))
            If IsTransparent(Id) OrElse IsDecor(Id) Then Return
            Dim Fluid As Boolean = IsFluid(Id)
            For i = 0 To 255
                If Filled(i) Then Continue For
                If HasFluid(i) Then
                    '地表已经是水，这一层只是海底
                    If Not Fluid AndAlso Under(i) = "" Then
                        Under(i) = Id
                        Filled(i) = True
                    End If
                    Continue For
                End If
                If Result(i) = "" Then
                    Result(i) = Id
                    '整段同一个方块，地表高度就是这一段的顶端
                    Heights(i) = CShort(SecY * 16 + 15)
                End If
                If Fluid Then
                    HasFluid(i) = True
                Else
                    Filled(i) = True
                End If
            Next
            Return
        End If
        Dim Longs As Long() = TryCast(DataTag.TagValue, Long())
        If Longs Is Nothing OrElse Longs.Length = 0 Then Return
        Dim ValuesPerLong As Integer = 64 \ Bits
        If ValuesPerLong <= 0 Then Return
        Dim Mask As Long = (1L << Bits) - 1
        '索引顺序是 y*256 + z*16 + x
        For i = 0 To 255
            If Filled(i) Then Continue For
            '这一列里从高到低找（section 内 y 15→0）
            Dim Picked As String = ""
            Dim PickedY As Integer = 0
            Dim FluidId As String = ""
            For Y As Integer = 15 To 0 Step -1
                Dim Index As Integer = Y * 256 + i
                Dim LongPos As Integer = Index \ ValuesPerLong
                If LongPos >= Longs.Length Then Continue For
                Dim Shift As Integer = (Index Mod ValuesPerLong) * Bits
                Dim PaletteIndex As Integer = CInt((Longs(LongPos) >> Shift) And Mask)
                If PaletteIndex < 0 OrElse PaletteIndex >= Palette.Count Then Continue For
                Dim Id As String = Palette(PaletteIndex)
                If IsTransparent(Id) OrElse IsDecor(Id) Then Continue For
                If HasFluid(i) Then
                    '地表已经是水，这一层只找海底
                    If Not IsFluid(Id) AndAlso Under(i) = "" Then
                        Under(i) = Id
                        Filled(i) = True
                        If Heights(i) = -999 Then Heights(i) = CShort(SecY * 16 + Y)
                        Exit For
                    End If
                    Continue For
                End If
                If FluidId = "" AndAlso IsFluid(Id) Then
                    '记下水面，继续找海底
                    FluidId = Id
                    Continue For
                End If
                If FluidId <> "" Then
                    '还在水里，继续往下找真正的海底
                    If IsFluid(Id) Then Continue For
                    If Under(i) = "" Then Under(i) = Id
                    Picked = FluidId
                    Exit For
                End If
                Picked = Id
                PickedY = Y
                Exit For
            Next
            If Picked <> "" Then
                Result(i) = Picked
                If Heights(i) = -999 Then Heights(i) = CShort(SecY * 16 + PickedY)
                If FluidId <> "" Then HasFluid(i) = True Else Filled(i) = True
            ElseIf FluidId <> "" Then
                '这一段只找到了水、没找到海底：水柱会继续到下面的 section，
                '必须把 HasFluid 传递下去，否则下一段会把海底当成地表（深水河就断在这里）。
                If Result(i) = "" Then Result(i) = FluidId
                HasFluid(i) = True
            End If
        Next
    End Sub

#Region "区域文件读取"

    ''' <summary>
    ''' 从 .mca 文件里读出指定槽位的区块，返回区块 NBT 根标签，区块不存在返回 Nothing。
    ''' </summary>
    Public Function ReadChunkFromMca(McaPath As String, Slot As Integer) As NbtTag
        If Slot < 0 OrElse Slot > 1023 Then Return Nothing
        If Not File.Exists(McaPath) Then Return Nothing
        Using Fs As New FileStream(McaPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            Dim Header(4095) As Byte
            If Fs.Read(Header, 0, 4096) < 4096 Then Return Nothing
            '头部前 4096 字节是位置表，每项 4 字节：3 字节扇区偏移 + 1 字节扇区数
            Dim Offset As Integer = (CInt(Header(Slot * 4)) << 16) Or (CInt(Header(Slot * 4 + 1)) << 8) Or CInt(Header(Slot * 4 + 2))
            Dim Sectors As Integer = CInt(Header(Slot * 4 + 3))
            If Offset = 0 OrElse Sectors = 0 Then Return Nothing
            Dim ByteOffset As Long = CLng(Offset) * 4096
            If ByteOffset + 5 > Fs.Length Then Return Nothing
            Fs.Position = ByteOffset
            '长度 4 字节 + 压缩类型 1 字节
            Dim LenBytes(3) As Byte
            If Fs.Read(LenBytes, 0, 4) < 4 Then Return Nothing
            Dim Length As Integer = (CInt(LenBytes(0)) << 24) Or (CInt(LenBytes(1)) << 16) Or (CInt(LenBytes(2)) << 8) Or CInt(LenBytes(3))
            If Length <= 1 OrElse ByteOffset + 4 + Length > Fs.Length Then Return Nothing
            Dim TypeByte(0) As Byte
            If Fs.Read(TypeByte, 0, 1) < 1 Then Return Nothing
            Dim Compression As Integer = CInt(TypeByte(0))
            Dim Payload(Length - 2) As Byte
            Dim ReadTotal As Integer = 0
            While ReadTotal < Payload.Length
                Dim N As Integer = Fs.Read(Payload, ReadTotal, Payload.Length - ReadTotal)
                If N <= 0 Then Exit While
                ReadTotal += N
            End While
            If ReadTotal = 0 Then Return Nothing
            Dim Raw As Byte() = DecompressChunk(Payload, ReadTotal, Compression)
            If Raw Is Nothing OrElse Raw.Length < 2 Then Return Nothing
            Return DstNbt.ParseBytes(Raw)
        End Using
    End Function

    ''' <summary>解压区块数据。1=GZip，2=Zlib，3=不压缩，4=LZ4（不处理，直接返回 Nothing）。</summary>
    Private Function DecompressChunk(Payload As Byte(), Count As Integer, Compression As Integer) As Byte()
        Try
            Select Case Compression
                Case 1
                    Using Ms As New MemoryStream(Payload, 0, Count)
                        Using Gz As New GZipStream(Ms, CompressionMode.Decompress)
                            Using Out As New MemoryStream
                                Gz.CopyTo(Out)
                                Return Out.ToArray()
                            End Using
                        End Using
                    End Using
                Case 2
                    '跳过 2 字节的 zlib 头
                    Using Ms As New MemoryStream(Payload, 2, Count - 2)
                        Using Def As New DeflateStream(Ms, CompressionMode.Decompress)
                            Using Out As New MemoryStream
                                Def.CopyTo(Out)
                                Return Out.ToArray()
                            End Using
                        End Using
                    End Using
                Case 3
                    Dim Copy(Count - 1) As Byte
                    Array.Copy(Payload, Copy, Count)
                    Return Copy
                Case Else
                    Return Nothing
            End Select
        Catch ex As Exception
            Logger.Info("解压区块失败：" & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>某个维度对应的区域文件夹。26.1 前后结构不同，交给 DstSavePaths 统一判断。</summary>
    Public Function RegionFolderOf(SaveDir As String, Dimension As String) As String
        Return DstSavePaths.RegionFolder(SaveDir, Dimension)
    End Function

#End Region

End Module
