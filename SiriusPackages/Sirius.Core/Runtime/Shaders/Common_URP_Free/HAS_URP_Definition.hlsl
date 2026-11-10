#ifndef SIRIUS_HAS_URP_DEFINITION_INCLUDED
#define SIRIUS_HAS_URP_DEFINITION_INCLUDED

// 空ではなく NO_URP を明示し、ビルド時に食い違う側のキーワードを持つバリアントだけを剥離できるようにする
#pragma multi_compile NO_URP HAS_URP

#endif
